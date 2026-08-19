using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine.SceneManagement;
using YooAsset;
using SceneHandle = YooAsset.SceneHandle;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景管理服务。支持主场景切换、附加场景加载/卸载、进度回调和挂起加载。
    /// </summary>
    public sealed class SceneService : ServiceBase, ISceneService
    {
        private string _currentMainSceneName = string.Empty;

        private SceneHandle _currentMainScene;

        private readonly Dictionary<string, SceneHandle> _subScenes = new Dictionary<string, SceneHandle>();

        private readonly HashSet<string> _handlingScene = new HashSet<string>();

        /// <summary>
        /// 当前主场景名称。
        /// </summary>
        public string CurrentMainSceneName => _currentMainSceneName;

        /// <summary>
        /// 服务初始化。
        /// </summary>
        public override void OnInit()
        {
            _currentMainScene = null;
            _currentMainSceneName = SceneManager.GetSceneByBuildIndex(0).name;
        }

        /// <summary>
        /// 服务释放，卸载所有子场景。
        /// </summary>
        public override void Shutdown()
        {
            var iter = _subScenes.Values.GetEnumerator();
            while (iter.MoveNext())
            {
                SceneHandle subScene = iter.Current;
                if (subScene != null)
                {
                    subScene.UnloadAsync();
                }
            }

            iter.Dispose();
            _subScenes.Clear();
            _handlingScene.Clear();
            _currentMainSceneName = string.Empty;
        }

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="progressCallBack">进度回调。</param>
        /// <returns>加载完成的场景。</returns>
        public async UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            bool gcCollect = true, Action<float> progressCallBack = null)
        {
            if (!_handlingScene.Add(location))
            {
                LogUtility.Error("Could not load scene while loading. Scene: {0}", location);
                return default;
            }

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (_subScenes.TryGetValue(location, out SceneHandle subScene))
                {
                    throw new GameException($"Could not load subScene while already loaded. Scene: {location}");
                }

                subScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                // 前置注册——subScene.IsDone 在 UnSuspend 之后才会是 true
                _subScenes.Add(location, subScene);

                await AwaitSceneHandle(subScene, progressCallBack);

                _handlingScene.Remove(location);

                return subScene.SceneObject;
            }
            else
            {
                if (_currentMainSceneName == location && _currentMainScene is { IsDone: false })
                {
                    throw new GameException($"Could not load MainScene while loading. CurrentMainScene: {_currentMainSceneName}.");
                }

                _currentMainSceneName = location;

                _currentMainScene = YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);

                await AwaitSceneHandle(_currentMainScene, progressCallBack);

#if UNITY_EDITOR && EditorFixedMaterialShader
                MaterialUtility.WaitGetRootGameObjects(_currentMainScene).Forget();
#endif

                GameServices.GetService<IResourceService>().ForceUnloadUnusedAssets(gcCollect);

                _handlingScene.Remove(location);

                return _currentMainScene.SceneObject;
            }
        }

        /// <summary>
        /// 同步加载场景（回调式）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="callBack">加载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, bool gcCollect = true, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null)
        {
            if (!_handlingScene.Add(location))
            {
                LogUtility.Error("Could not load scene while loading. Scene: {0}", location);
                return;
            }

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (_subScenes.TryGetValue(location, out SceneHandle subScene))
                {
                    throw new GameException($"Could not load subScene while already loaded. Scene: {location}");
                }

                subScene = CreateSceneHandle(location, packageName, sceneMode, suspendLoad, priority);

                subScene.Completed += handle =>
                {
                    _handlingScene.Remove(location);
                    callBack?.Invoke(handle.SceneObject);
                };

                if (progressCallBack != null)
                {
                    InvokeProgress(subScene, progressCallBack).Forget();
                }

                _subScenes.Add(location, subScene);
            }
            else
            {
                if (_currentMainSceneName == location && _currentMainScene is { IsDone: false })
                {
                    throw new GameException($"Could not load MainScene while loading. CurrentMainScene: {_currentMainSceneName}.");
                }

                _currentMainSceneName = location;

                _currentMainScene = CreateSceneHandle(location, packageName, sceneMode, suspendLoad, priority);

                _currentMainScene.Completed += handle =>
                {
                    _handlingScene.Remove(location);
                    callBack?.Invoke(handle.SceneObject);
                };

                if (progressCallBack != null)
                {
                    InvokeProgress(_currentMainScene, progressCallBack).Forget();
                }

#if UNITY_EDITOR && EditorFixedMaterialShader
                MaterialUtility.WaitGetRootGameObjects(_currentMainScene).Forget();
#endif

                GameServices.GetService<IResourceService>().ForceUnloadUnusedAssets(gcCollect);
            }
        }

        /// <summary>
        /// 创建场景句柄。根据 packageName 选择默认包或指定包。
        /// </summary>
        private static SceneHandle CreateSceneHandle(string location, string packageName, LoadSceneMode sceneMode, bool suspendLoad, uint priority)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadSceneAsync(location, sceneMode, LocalPhysicsMode.None, suspendLoad, priority);
        }

        /// <summary>
        /// 等待场景句柄完成，可选进度回调。
        /// </summary>
        private static async UniTask AwaitSceneHandle(SceneHandle handle, Action<float> progressCallBack)
        {
            if (progressCallBack != null)
            {
                while (!handle.IsDone && handle.IsValid)
                {
                    progressCallBack.Invoke(handle.Progress);
                    await UniTask.Yield();
                }
            }
            else
            {
                await handle.ToUniTask();
            }
        }

        /// <summary>
        /// 轮询场景句柄进度（回调式加载用）。
        /// </summary>
        private static async UniTaskVoid InvokeProgress(SceneHandle sceneHandle, Action<float> progress)
        {
            if (sceneHandle == null)
            {
                return;
            }

            while (!sceneHandle.IsDone && sceneHandle.IsValid)
            {
                await UniTask.Yield();

                progress?.Invoke(sceneHandle.Progress);
            }
        }

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否激活成功。</returns>
        public bool ActivateScene(string location)
        {
            if (_currentMainSceneName.Equals(location))
            {
                return _currentMainScene != null && _currentMainScene.ActivateScene();
            }

            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                return subScene.ActivateScene();
            }

            LogUtility.Warning("ActivateScene invalid location:{0}", location);
            return false;
        }

        /// <summary>
        /// 取消挂起。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        public bool UnSuspend(string location)
        {
            if (_currentMainSceneName.Equals(location))
            {
                return _currentMainScene != null && _currentMainScene.UnSuspend();
            }

            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                return subScene.UnSuspend();
            }

            LogUtility.Warning("UnSuspend invalid location:{0}", location);
            return false;
        }

        /// <summary>
        /// 判断指定场景是否为当前主场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否为主场景。</returns>
        public bool IsMainScene(string location)
        {
            UnityEngine.SceneManagement.Scene currentScene = SceneManager.GetActiveScene();

            if (_currentMainSceneName.Equals(location))
            {
                if (_currentMainScene == null)
                {
                    return false;
                }
                return currentScene.name == _currentMainScene.SceneName;
            }

            // 不是请求的主场景，但当前激活场景可能就是主场景
            if (_currentMainScene != null && currentScene.name == _currentMainScene.SceneName)
            {
                return true;
            }

            LogUtility.Warning("IsMainScene invalid location:{0}", location);
            return false;
        }

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="progressCallBack">进度回调。</param>
        /// <returns>是否卸载成功。</returns>
        public async UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null)
        {
            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    LogUtility.Error("Could not unload Scene while not loaded. Scene: {0}", location);
                    return false;
                }

                if (!_handlingScene.Add(location))
                {
                    LogUtility.Warning("Could not unload Scene while loading. Scene: {0}", location);
                    return false;
                }

                var unloadOperation = subScene.UnloadAsync();

                if (progressCallBack != null)
                {
                    while (!unloadOperation.IsDone && unloadOperation.Status != EOperationStatus.Failed)
                    {
                        progressCallBack.Invoke(unloadOperation.Progress);
                        await UniTask.Yield();
                    }
                }
                else
                {
                    await unloadOperation.ToUniTask();
                }

                _subScenes.Remove(location);

                _handlingScene.Remove(location);

                return true;
            }

            LogUtility.Warning("UnloadAsync invalid location:{0}", location);
            return false;
        }

        /// <summary>
        /// 卸载子场景（回调式）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public void Unload(string location, Action callBack = null, Action<float> progressCallBack = null)
        {
            _subScenes.TryGetValue(location, out SceneHandle subScene);
            if (subScene != null)
            {
                if (subScene.SceneObject == default)
                {
                    LogUtility.Error("Could not unload Scene while not loaded. Scene: {0}", location);
                    return;
                }

                if (!_handlingScene.Add(location))
                {
                    LogUtility.Warning("Could not unload Scene while loading. Scene: {0}", location);
                    return;
                }

                var unloadOperation = subScene.UnloadAsync();
                unloadOperation.Completed += _ =>
                {
                    _subScenes.Remove(location);
                    _handlingScene.Remove(location);
                    callBack?.Invoke();
                };

                if (progressCallBack != null)
                {
                    InvokeProgress(subScene, progressCallBack).Forget();
                }

                return;
            }

            LogUtility.Warning("Unload invalid location:{0}", location);
        }

        #endregion

        /// <summary>
        /// 查询场景是否已加载。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否已加载。</returns>
        public bool IsContainScene(string location)
        {
            if (_currentMainSceneName.Equals(location))
            {
                return true;
            }

            return _subScenes.TryGetValue(location, out var _);
        }
    }
}