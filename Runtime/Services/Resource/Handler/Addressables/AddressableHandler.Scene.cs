#if ADDRESSABLES_INSTALLED
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// <see cref="AddressableHandler"/> 的场景加载部分——场景经 Addressables 管线加载，产出 <see cref="ResourceSceneHandle"/> 适配句柄。
    /// </summary>
    partial class AddressableHandler
    {
        #region 场景加载 [SCENE LOADING]

        /// <inheritdoc />
        public override ResourceSceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "")
        {
            var handle = Addressables.LoadSceneAsync(location, sceneMode, !suspendLoad, (int)priority);
            return new AddressableSceneHandleAdapter(handle);
        }

        /// <summary>
        /// Addressables 场景句柄适配器，把 Addressables 的挂起语义映射到 <see cref="ResourceSceneHandle"/> 契约。
        /// </summary>
        /// <remarks>
        /// activateOnLoad=false 时外层句柄在场景就绪（待激活）即完成，激活需显式调用 <see cref="SceneInstance.ActivateAsync"/>；
        /// <c>IsDone</c> 与 <c>SceneObject</c> 均以 <see cref="Scene.isLoaded"/>（激活完成标记）为准，未完成时 <c>Progress</c> 封顶 0.99。
        /// </remarks>
        private sealed class AddressableSceneHandleAdapter : ResourceSceneHandle
        {
            private AsyncOperationHandle<SceneInstance> _handle;

            public AddressableSceneHandleAdapter(AsyncOperationHandle<SceneInstance> handle)
            {
                _handle = handle;
            }

            /// <inheritdoc />
            public override bool IsDone => !_handle.IsValid() || (_handle.IsDone && (_handle.Status != AsyncOperationStatus.Succeeded || _handle.Result.Scene.isLoaded));

            /// <inheritdoc />
            public override float Progress => !_handle.IsValid()
                ? 1f
                : _handle.Status == AsyncOperationStatus.Succeeded && !_handle.Result.Scene.isLoaded
                    ? 0.99f
                    : _handle.PercentComplete;

            /// <inheritdoc />
            public override string Error => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Failed
                ? _handle.OperationException?.Message
                : string.Empty;

            /// <inheritdoc />
            public override UnityEngine.SceneManagement.Scene SceneObject => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded && _handle.Result.Scene.isLoaded
                ? _handle.Result.Scene
                : default;

            /// <inheritdoc />
            public override bool UnSuspend()
            {
                if (!_handle.IsValid() || _handle.Status != AsyncOperationStatus.Succeeded)
                {
                    return false;
                }

                _handle.Result.ActivateAsync();
                return true;
            }

            /// <inheritdoc />
            public override bool ActivateScene()
            {
                if (!_handle.IsValid() || _handle.Status != AsyncOperationStatus.Succeeded)
                {
                    return false;
                }

                return SceneManager.SetActiveScene(_handle.Result.Scene);
            }

            /// <inheritdoc />
            public override IResourceOperation UnloadAsync()
            {
                if (!_handle.IsValid())
                {
                    return null;
                }

                return new AddressableOperationAdapter(Addressables.UnloadSceneAsync(_handle));
            }

            /// <inheritdoc />
            public override void Release()
            {
                if (_handle.IsValid())
                {
                    Addressables.Release(_handle);
                }
            }
        }

        #endregion

        #region 框架抽象适配 [FRAMEWORK ADAPTERS]

        /// <summary>
        /// Addressables 异步操作适配器。
        /// </summary>
        private sealed class AddressableOperationAdapter : IResourceOperation
        {
            private readonly AsyncOperationHandle _handle;

            public AddressableOperationAdapter(AsyncOperationHandle handle)
            {
                _handle = handle;
            }

            public bool IsDone => _handle.IsDone;
            public float Progress => _handle.PercentComplete;
            public bool Succeed => _handle.Status == AsyncOperationStatus.Succeeded;
            public string Error => _handle.Status == AsyncOperationStatus.Failed ? _handle.OperationException?.Message : null;
        }

        #endregion
    }
}
#endif
