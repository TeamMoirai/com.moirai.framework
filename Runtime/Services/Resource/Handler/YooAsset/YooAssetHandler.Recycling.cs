using System;
using System.Collections.Generic;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// YooAsset 后端的回收面：低内存回调、包级未用/全量卸载操作与代际清空。
    /// </summary>
    /// <remarks>强制全量回收走"清空后继续用同一实例"的口径：卸载代际自增、加载中操作与绑定服务同步作废。</remarks>
    partial class YooAssetHandler
    {
        #region 资源回收 [ASSET RECYCLING]

        /// <inheritdoc />
        public override void OnLowMemory()
        {
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        private readonly List<UnloadUnusedAssetsOperation> _unloadUnusedAssetsOperations = new List<UnloadUnusedAssetsOperation>();

        private readonly List<UnloadAllAssetsOperation> _unloadAllAssetsOperations = new List<UnloadAllAssetsOperation>();

        /// <inheritdoc />
        public override void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets()
        {
            UnloadUnusedAssets(false);
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets(bool force)
        {
            if (force)
            {
                ReleaseAllUnusedAssetRecords();
            }

            RemoveCompletedUnloadUnusedOperations();
            if (_unloadUnusedAssetsOperations.Count > 0)
            {
                return;
            }

            _assetInfoMap.Clear();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true })
                {
                    _unloadUnusedAssetsOperations.Add(package.UnloadUnusedAssetsAsync());
                }
            }
        }

        /// <inheritdoc />
        public override void ForceUnloadAllAssets()
        {
#if UNITY_WEBGL
            LogUtility.Warning("WebGL not support invoke {0}", nameof(ForceUnloadAllAssets));
#else
            RemoveCompletedUnloadAllOperations();
            if (_unloadAllAssetsOperations.Count > 0)
            {
                return;
            }

            unchecked
            {
                Store._unloadGeneration++;
            }

            Store.ShutdownLoadingOperations();
            if (_bindingService == null)
            {
                _bindingService = new ResourceBindingService(this);
            }
            else
            {
                // 这里是"清空后继续用同一实例"，走 Reset 而非终态 Shutdown。
                _bindingService.Reset();
            }

            ForceReleaseAllAssetRecords();
            WarmupBindingRecords();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true })
                {
                    _unloadAllAssetsOperations.Add(package.UnloadAllAssetsAsync());
                }
            }
#endif
        }

        /// <inheritdoc />
        public override void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssetsAction?.Invoke(performGCCollect);
        }

        private void RemoveCompletedUnloadUnusedOperations()
        {
            for (int i = _unloadUnusedAssetsOperations.Count - 1; i >= 0; i--)
            {
                UnloadUnusedAssetsOperation operation = _unloadUnusedAssetsOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _unloadUnusedAssetsOperations.RemoveAt(i);
                }
            }
        }

        private void RemoveCompletedUnloadAllOperations()
        {
            for (int i = _unloadAllAssetsOperations.Count - 1; i >= 0; i--)
            {
                UnloadAllAssetsOperation operation = _unloadAllAssetsOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _unloadAllAssetsOperations.RemoveAt(i);
                }
            }
        }

        #endregion
    }
}
