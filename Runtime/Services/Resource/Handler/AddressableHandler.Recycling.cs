#if ADDRESSABLES_INSTALLED
using System;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的回收面：低内存转发与强制档回收。
    /// </summary>
    /// <remarks>非强制档在 YooAsset 侧推进的是 bundle 卸载操作，Addressables 没有对应物，只接"强制档还掉零引用记录"这一半。</remarks>
    partial class AddressableHandler
    {
        #region 资源回收 [ASSET RECYCLING]

        /// <inheritdoc />
        public override void OnLowMemory()
        {
            // 这份委托由 ResourceService 初始化时登记进来（RequestForceUnloadUnusedAssets）。
            // 吞掉它会让 Application.lowMemory 在本后端失效：调用方照旧返回，
            // 只是再没有人在内存吃紧时请求强制回收。
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

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
            // 非强制档在 YooAsset 侧推进的是 bundle 卸载操作，Addressables 没有对应物；
            // 能对上的是"强制档还掉引用计数为零的记录"，所以只接这一半。
            if (force)
            {
                ReleaseAllUnusedAssetRecords();
            }
        }

        /// <inheritdoc />
        public override void ForceUnloadAllAssets()
        {
            ReleaseAllUnusedAssetRecords();
        }

        /// <inheritdoc />
        public override void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            ReleaseAllUnusedAssetRecords();
        }

        #endregion
    }
}
#endif
