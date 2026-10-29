using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Assets.Unity
{
    public sealed class ResourcesAssetLoader : IAssetLoader
    {
        public async UniTask<IAssetPayload<T>> LoadAssetAsync<T>(ResourceKey key,
            CancellationToken ownerToken) where T : UnityEngine.Object
        {
            ResourceRequest request = Resources.LoadAsync<T>(key.Location);
            await request.ToUniTask(cancellationToken: ownerToken);
            ownerToken.ThrowIfCancellationRequested();
            T asset = request.asset as T;
            if (asset == null)
                throw new InvalidOperationException("Resource is missing or of the wrong type: " + key);
            return new AssetPayload<T>(asset);
        }

        public async UniTask<IScenePayload> LoadSceneAsync(ResourceKey key,
            LoadSceneParameters parameters, bool activateOnLoad, CancellationToken ownerToken)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(key.Location, parameters);
            if (operation == null)
                throw new InvalidOperationException("Scene could not be requested: " + key);
            operation.allowSceneActivation = activateOnLoad;
            if (!activateOnLoad)
            {
                await UniTask.WaitUntil(() => operation.progress >= 0.9f,
                    cancellationToken: ownerToken);
            }
            else
            {
                await operation.ToUniTask(cancellationToken: ownerToken);
            }
            return new ScenePayload(operation, SceneManager.GetSceneByPath(key.Location),
                !activateOnLoad);
        }

        private sealed class AssetPayload<T> : IAssetPayload<T> where T : UnityEngine.Object
        {
            private T _asset;
            private bool _disposed;

            internal AssetPayload(T asset) { _asset = asset; }

            public T Asset => _asset;

            public UniTask DisposeAsync()
            {
                if (_disposed) return UniTask.CompletedTask;
                _disposed = true;
                T asset = _asset;
                _asset = null;
                if (asset != null && !(asset is GameObject) && !(asset is Component))
                    Resources.UnloadAsset(asset);
                return UniTask.CompletedTask;
            }
        }

        private sealed class ScenePayload : IScenePayload
        {
            private AsyncOperation _operation;
            private UnityEngine.SceneManagement.Scene _scene;
            private bool _suspended;
            private bool _disposed;

            internal ScenePayload(AsyncOperation operation, UnityEngine.SceneManagement.Scene scene, bool suspended)
            {
                _operation = operation;
                _scene = scene;
                _suspended = suspended;
            }

            public UnityEngine.SceneManagement.Scene Scene => _scene;

            public async UniTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                if (_suspended && _operation != null)
                {
                    _operation.allowSceneActivation = true;
                    await _operation.ToUniTask();
                    _suspended = false;
                }
                if (_scene.IsValid())
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(_scene);
                    if (unload != null) await unload.ToUniTask();
                }
                _operation = null;
            }
        }
    }
}
