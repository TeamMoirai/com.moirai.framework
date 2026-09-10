using UnityEngine.SceneManagement;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// <see cref="YooAssetHandler"/> 的场景加载部分——场景经 YooAsset 资源管线加载，产出 <see cref="ResourceSceneHandle"/> 适配句柄。
    /// </summary>
    public sealed partial class YooAssetHandler
    {
        /// <inheritdoc />
        public override ResourceSceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "")
        {
            var package = GetPackageOrThrow(packageName);
            var handle = package.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, !suspendLoad, priority);
            return new YooAssetSceneHandleAdapter(handle);
        }

        /// <summary>
        /// YooAsset 场景句柄适配器。
        /// </summary>
        private sealed class YooAssetSceneHandleAdapter : ResourceSceneHandle
        {
            private YooAsset.SceneHandle _handle;

            public YooAssetSceneHandleAdapter(YooAsset.SceneHandle handle)
            {
                _handle = handle;
            }

            /// <inheritdoc />
            public override bool IsDone => _handle == null || _handle.IsDone;

            /// <inheritdoc />
            public override float Progress => _handle?.Progress ?? 1f;

            /// <inheritdoc />
            public override string Error => _handle?.Error ?? string.Empty;

            /// <inheritdoc />
            public override UnityEngine.SceneManagement.Scene SceneObject => _handle?.SceneObject ?? default;

            /// <inheritdoc />
            public override bool UnSuspend()
            {
                return _handle != null && _handle.AllowSceneActivation();
            }

            /// <inheritdoc />
            public override bool ActivateScene()
            {
                return _handle != null && _handle.ActivateScene();
            }

            /// <inheritdoc />
            public override IResourceOperation UnloadAsync()
            {
                if (_handle == null)
                {
                    return null;
                }

                // 场景卸载成功后 YooAsset 自动释放该句柄的引用计数，本地同步置空防止再访问。
                var operation = _handle.UnloadSceneAsync();
                _handle = null;
                return new YooAssetOperationAdapter(operation);
            }

            /// <inheritdoc />
            public override void Release()
            {
                _handle?.Release();
                _handle = null;
            }
        }
    }
}
