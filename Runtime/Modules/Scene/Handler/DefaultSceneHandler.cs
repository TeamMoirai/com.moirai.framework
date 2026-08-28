using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 默认场景处理器实现。
    /// <para><see cref="SceneServiceHandler"/> 的内置实现，承载主场景切换、附加场景加载/卸载、进度回调和挂起加载等核心逻辑。</para>
    /// <para>由 <see cref="SceneServiceSettings"/> 序列化配置，可替换为自定义场景加载后端。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultSceneHandler : SceneServiceHandler
    {
        private string _currentMainSceneName = string.Empty;

        private readonly Dictionary<string, AsyncOperation> _loadingOperations = new Dictionary<string, AsyncOperation>();

        private readonly HashSet<string> _subScenes = new HashSet<string>();

        private readonly HashSet<string> _handlingScene = new HashSet<string>();

        /// <summary>
        /// 当前主场景名称。
        /// </summary>
        public override string CurrentMainSceneName => _currentMainSceneName;

        /// <summary>
        /// 处理器初始化。
        /// </summary>
        protected override void OnInit()
        {
            _currentMainSceneName = SceneManager.GetSceneByBuildIndex(0).name;
        }

        /// <summary>
        /// 处理器关闭，卸载所有子场景。
        /// </summary>
        protected override void OnShutdown()
        {
            foreach (var location in _subScenes)
            {
                var scene = SceneManager.GetSceneByName(location);
                if (scene.IsValid())
                {
                    SceneManager.UnloadSceneAsync(scene);
                }
            }

            _subScenes.Clear();
            _loadingOperations.Clear();
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
        public override async UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority,
            bool gcCollect, Action<float> progressCallBack)
        {
            if (!_handlingScene.Add(location))
            {
                LogUtility.Error("Could not load scene while loading. Scene: {0}", location);
                return default;
            }

            // 预加载场景资源
            await ResourceService.LoadLeaseAsync<UnityEngine.Object>(location);

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (_subScenes.Contains(location))
                {
                    throw new GameException($"Could not load subScene while already loaded. Scene: {location}");
                }

                var asyncOp = SceneManager.LoadSceneAsync(location, sceneMode);
                asyncOp.allowSceneActivation = !suspendLoad;
                _loadingOperations[location] = asyncOp;

                // 前置注册——场景在 UnSuspend 之后才会完成加载
                _subScenes.Add(location);

                await AwaitSceneOperation(asyncOp, progressCallBack);

                _loadingOperations.Remove(location);
                _handlingScene.Remove(location);

                return SceneManager.GetSceneByName(location);
            }
            else
            {
                _currentMainSceneName = location;

                var asyncOp = SceneManager.LoadSceneAsync(location, sceneMode);
                asyncOp.allowSceneActivation = !suspendLoad;
                _loadingOperations[location] = asyncOp;

                await AwaitSceneOperation(asyncOp, progressCallBack);

                _loadingOperations.Remove(location);

                var scene = SceneManager.GetSceneByName(location);

#if UNITY_EDITOR && EditorFixedMaterialShader
                MaterialUtility.WaitGetRootGameObjects(scene).Forget();
#endif

                ResourceService.ForceUnloadUnusedAssets(gcCollect);

                _handlingScene.Remove(location);

                return scene;
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
        public override void LoadScene(string location, string packageName, LoadSceneMode sceneMode,
            bool suspendLoad, uint priority, bool gcCollect, Action<UnityEngine.SceneManagement.Scene> callBack, Action<float> progressCallBack)
        {
            if (!_handlingScene.Add(location))
            {
                LogUtility.Error("Could not load scene while loading. Scene: {0}", location);
                return;
            }

            LoadSceneInternal(location, sceneMode, suspendLoad, gcCollect, callBack, progressCallBack).Forget();
        }

        /// <summary>
        /// 内部异步加载场景（回调式）。
        /// </summary>
        private async UniTaskVoid LoadSceneInternal(string location, LoadSceneMode sceneMode, bool suspendLoad, bool gcCollect,
            Action<UnityEngine.SceneManagement.Scene> callBack, Action<float> progressCallBack)
        {
            // 预加载场景资源
            await ResourceService.LoadLeaseAsync<UnityEngine.Object>(location);

            if (sceneMode == LoadSceneMode.Additive)
            {
                if (_subScenes.Contains(location))
                {
                    throw new GameException($"Could not load subScene while already loaded. Scene: {location}");
                }

                var asyncOp = SceneManager.LoadSceneAsync(location, sceneMode);
                asyncOp.allowSceneActivation = !suspendLoad;
                _loadingOperations[location] = asyncOp;

                // 前置注册——场景在 UnSuspend 之后才会完成加载
                _subScenes.Add(location);

                if (progressCallBack != null)
                {
                    InvokeProgress(asyncOp, progressCallBack).Forget();
                }

                asyncOp.completed += _ =>
                {
                    _loadingOperations.Remove(location);
                    _handlingScene.Remove(location);
                    callBack?.Invoke(SceneManager.GetSceneByName(location));
                };
            }
            else
            {
                _currentMainSceneName = location;

                var asyncOp = SceneManager.LoadSceneAsync(location, sceneMode);
                asyncOp.allowSceneActivation = !suspendLoad;
                _loadingOperations[location] = asyncOp;

                if (progressCallBack != null)
                {
                    InvokeProgress(asyncOp, progressCallBack).Forget();
                }

                asyncOp.completed += _ =>
                {
                    _loadingOperations.Remove(location);
                    _handlingScene.Remove(location);

                    var scene = SceneManager.GetSceneByName(location);

#if UNITY_EDITOR && EditorFixedMaterialShader
                    MaterialUtility.WaitGetRootGameObjects(scene).Forget();
#endif

                    ResourceService.ForceUnloadUnusedAssets(gcCollect);

                    callBack?.Invoke(scene);
                };
            }
        }

        /// <summary>
        /// 等待场景加载操作完成，可选进度回调。
        /// </summary>
        private static async UniTask AwaitSceneOperation(AsyncOperation asyncOp, Action<float> progressCallBack)
        {
            if (progressCallBack != null)
            {
                while (!asyncOp.isDone)
                {
                    progressCallBack.Invoke(asyncOp.progress);
                    await UniTask.Yield();
                }
            }
            else
            {
                while (!asyncOp.isDone)
                {
                    await UniTask.Yield();
                }
            }
        }

        /// <summary>
        /// 轮询场景加载进度（回调式加载用）。
        /// </summary>
        private static async UniTaskVoid InvokeProgress(AsyncOperation asyncOp, Action<float> progress)
        {
            if (asyncOp == null)
            {
                return;
            }

            while (!asyncOp.isDone)
            {
                await UniTask.Yield();
                progress?.Invoke(asyncOp.progress);
            }
        }

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否激活成功。</returns>
        public override bool ActivateScene(string location)
        {
            var scene = SceneManager.GetSceneByName(location);
            if (scene.IsValid())
            {
                return SceneManager.SetActiveScene(scene);
            }

            LogUtility.Warning("ActivateScene invalid location:{0}", location);
            return false;
        }

        /// <summary>
        /// 取消挂起。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        public override bool UnSuspend(string location)
        {
            if (_loadingOperations.TryGetValue(location, out var asyncOp))
            {
                asyncOp.allowSceneActivation = true;
                return true;
            }

            LogUtility.Warning("UnSuspend invalid location:{0}", location);
            return false;
        }

        /// <summary>
        /// 判断指定场景是否为当前主场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否为主场景。</returns>
        public override bool IsMainScene(string location)
        {
            var currentScene = SceneManager.GetActiveScene();

            if (_currentMainSceneName.Equals(location))
            {
                return currentScene.name == _currentMainSceneName;
            }

            // 不是请求的主场景，但当前激活场景可能就是主场景
            if (currentScene.name == _currentMainSceneName)
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
        public override async UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack)
        {
            if (!_subScenes.Contains(location))
            {
                LogUtility.Warning("UnloadAsync invalid location:{0}", location);
                return false;
            }

            if (!_handlingScene.Add(location))
            {
                LogUtility.Warning("Could not unload Scene while loading. Scene: {0}", location);
                return false;
            }

            var scene = SceneManager.GetSceneByName(location);
            if (!scene.IsValid())
            {
                LogUtility.Error("Could not unload Scene while not loaded. Scene: {0}", location);
                _handlingScene.Remove(location);
                return false;
            }

            var unloadOp = SceneManager.UnloadSceneAsync(scene);

            if (progressCallBack != null)
            {
                while (!unloadOp.isDone)
                {
                    progressCallBack.Invoke(unloadOp.progress);
                    await UniTask.Yield();
                }
            }
            else
            {
                while (!unloadOp.isDone)
                {
                    await UniTask.Yield();
                }
            }

            _subScenes.Remove(location);
            _handlingScene.Remove(location);

            return true;
        }

        /// <summary>
        /// 卸载子场景（回调式）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public override void Unload(string location, Action callBack, Action<float> progressCallBack)
        {
            if (!_subScenes.Contains(location))
            {
                LogUtility.Warning("Unload invalid location:{0}", location);
                return;
            }

            if (!_handlingScene.Add(location))
            {
                LogUtility.Warning("Could not unload Scene while loading. Scene: {0}", location);
                return;
            }

            var scene = SceneManager.GetSceneByName(location);
            if (!scene.IsValid())
            {
                LogUtility.Error("Could not unload Scene while not loaded. Scene: {0}", location);
                _handlingScene.Remove(location);
                return;
            }

            var unloadOp = SceneManager.UnloadSceneAsync(scene);

            if (progressCallBack != null)
            {
                InvokeProgress(unloadOp, progressCallBack).Forget();
            }

            unloadOp.completed += _ =>
            {
                _subScenes.Remove(location);
                _handlingScene.Remove(location);
                callBack?.Invoke();
            };
        }

        #endregion

        /// <summary>
        /// 查询场景是否已加载。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否已加载。</returns>
        public override bool IsContainScene(string location)
        {
            if (_currentMainSceneName.Equals(location))
            {
                return true;
            }

            return _subScenes.Contains(location);
        }
    }
}
