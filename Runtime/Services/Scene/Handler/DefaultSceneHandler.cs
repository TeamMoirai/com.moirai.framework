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
    /// <para>场景加载经由 <see cref="ResourceService"/> 走资源系统管线——自动应用当前配置的资源后端适配器（YooAsset、Addressable 等），
    /// 场景资源与普通资源共享包管理、下载与引用计数体系，而非引擎内建 <see cref="SceneManager"/> 加载管线。</para>
    /// <para>由 <see cref="SceneServiceSettings"/> 序列化配置，可替换为自定义场景加载后端。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultSceneHandler : SceneServiceHandler
    {
        [NonSerialized] private string _currentMainSceneName = string.Empty;

        /// <summary>
        /// 当前主场景句柄（Single 模式加载，被替换时释放引用计数）。
        /// </summary>
        [NonSerialized] private ResourceSceneHandle _mainSceneHandle;

        /// <summary>
        /// 在途场景加载（含挂起待激活），加载完成后移除。
        /// </summary>
        [NonSerialized] private readonly Dictionary<string, ResourceSceneHandle> _loadingScenes = new Dictionary<string, ResourceSceneHandle>();

        /// <summary>
        /// 已加载子场景句柄（location → 句柄），用于激活与卸载。
        /// </summary>
        [NonSerialized] private readonly Dictionary<string, ResourceSceneHandle> _subSceneHandles = new Dictionary<string, ResourceSceneHandle>();

        [NonSerialized] private readonly HashSet<string> _subScenes = new HashSet<string>();

        [NonSerialized] private readonly HashSet<string> _handlingScene = new HashSet<string>();

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
        /// 处理器关闭，卸载所有子场景并释放主场景句柄。
        /// </summary>
        protected override void OnShutdown()
        {
            foreach (var pair in _subSceneHandles)
            {
                pair.Value.UnloadAsync();
            }

            _subScenes.Clear();
            _subSceneHandles.Clear();
            _loadingScenes.Clear();
            _handlingScene.Clear();
            _mainSceneHandle?.Release();
            _mainSceneHandle = null;
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
        public override UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority,
            bool gcCollect, Action<float> progressCallBack)
        {
            if (!_handlingScene.Add(location))
            {
                LogUtility.Error("Could not load scene while loading. Scene: {0}", location);
                return UniTask.FromResult(default(UnityEngine.SceneManagement.Scene));
            }

            // 直通返回内部任务，避免外层 async 多分配一个状态机
            return LoadSceneInternal(location, string.Empty, sceneMode, suspendLoad, priority, gcCollect, progressCallBack);
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

            LoadSceneCallbackInternal(location, packageName ?? string.Empty, sceneMode, suspendLoad, priority, gcCollect, callBack, progressCallBack).Forget();
        }

        /// <summary>
        /// 回调式加载包装——复用核心加载流程，完成后触发回调。
        /// </summary>
        private async UniTaskVoid LoadSceneCallbackInternal(string location, string packageName, LoadSceneMode sceneMode,
            bool suspendLoad, uint priority, bool gcCollect, Action<UnityEngine.SceneManagement.Scene> callBack, Action<float> progressCallBack)
        {
            var scene = await LoadSceneInternal(location, packageName, sceneMode, suspendLoad, priority, gcCollect, progressCallBack);
            callBack?.Invoke(scene);
        }

        /// <summary>
        /// 场景加载核心流程——经 <see cref="ResourceService"/> 走资源系统管线加载场景。
        /// <para>进入前须已通过 <see cref="_handlingScene"/> 防重入检查；加载失败时清理登记并释放句柄。</para>
        /// </summary>
        private async UniTask<UnityEngine.SceneManagement.Scene> LoadSceneInternal(string location, string packageName, LoadSceneMode sceneMode,
            bool suspendLoad, uint priority, bool gcCollect, Action<float> progressCallBack)
        {
            if (sceneMode == LoadSceneMode.Additive && _subScenes.Contains(location))
            {
                _handlingScene.Remove(location);
                throw new GameException($"Could not load subScene while already loaded. Scene: {location}");
            }

            ResourceSceneHandle handle;
            try
            {
                handle = ResourceService.LoadSceneAsync(location, sceneMode, suspendLoad, priority, packageName);
            }
            catch (Exception)
            {
                // 资源后端同步失败（如资源包未初始化）：清理防重入标记后上抛
                _handlingScene.Remove(location);
                throw;
            }

            if (handle == null)
            {
                _handlingScene.Remove(location);
                LogUtility.Error("Could not load scene. Resource service is not ready. Scene: {0}", location);
                return default;
            }

            _loadingScenes[location] = handle;

            if (sceneMode == LoadSceneMode.Additive)
            {
                // 前置注册——挂起加载的场景在 UnSuspend 之后才会完成加载
                _subScenes.Add(location);
            }

            await AwaitSceneHandle(handle, progressCallBack);

            _loadingScenes.Remove(location);

            if (!string.IsNullOrEmpty(handle.Error))
            {
                LogUtility.Error("Could not load scene : {0}, error : {1}", location, handle.Error);
                handle.Release();
                _subScenes.Remove(location);
                _handlingScene.Remove(location);
                return default;
            }

            if (sceneMode == LoadSceneMode.Additive)
            {
                _subSceneHandles[location] = handle;
                _handlingScene.Remove(location);
                return handle.SceneObject;
            }

            // 主场景切换：新场景已激活，旧场景由引擎卸载，此处释放旧句柄回收底层资源引用计数
            _mainSceneHandle?.Release();
            _mainSceneHandle = handle;
            _currentMainSceneName = location;

            var scene = handle.SceneObject;

#if UNITY_EDITOR && EditorFixedMaterialShader
            MaterialUtility.WaitGetRootGameObjects(scene).Forget();
#endif

            ResourceService.ForceUnloadUnusedAssets(gcCollect);

            _handlingScene.Remove(location);

            return scene;
        }

        /// <summary>
        /// 等待场景加载句柄完成，可选进度回调。
        /// </summary>
        private static async UniTask AwaitSceneHandle(ResourceSceneHandle handle, Action<float> progressCallBack)
        {
            if (progressCallBack != null)
            {
                while (!handle.IsDone)
                {
                    progressCallBack.Invoke(handle.Progress);
                    await UniTask.Yield();
                }
            }
            else
            {
                while (!handle.IsDone)
                {
                    await UniTask.Yield();
                }
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
            if (_subSceneHandles.TryGetValue(location, out var subSceneHandle))
            {
                return subSceneHandle.ActivateScene();
            }

            if (_mainSceneHandle != null && _currentMainSceneName.Equals(location))
            {
                return _mainSceneHandle.ActivateScene();
            }

            // 回退：非资源系统加载的场景（如启动场景）按名称激活
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
            if (_loadingScenes.TryGetValue(location, out var handle))
            {
                return handle.UnSuspend();
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
        public override UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack)
        {
            if (!TryBeginUnload(location, out var handle))
            {
                return UniTask.FromResult(false);
            }

            return UnloadSceneInternal(location, handle, progressCallBack);
        }

        /// <summary>
        /// 卸载子场景（回调式）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public override void Unload(string location, Action callBack, Action<float> progressCallBack)
        {
            if (!TryBeginUnload(location, out var handle))
            {
                return;
            }

            UnloadSceneCallbackInternal(location, handle, callBack, progressCallBack).Forget();
        }

        /// <summary>
        /// 卸载前置检查——校验子场景已加载且未在途操作，通过后占用防重入标记。
        /// </summary>
        private bool TryBeginUnload(string location, out ResourceSceneHandle handle)
        {
            handle = null;

            if (!_subScenes.Contains(location))
            {
                LogUtility.Warning("Unload invalid location:{0}", location);
                return false;
            }

            if (!_handlingScene.Add(location))
            {
                LogUtility.Warning("Could not unload Scene while loading. Scene: {0}", location);
                return false;
            }

            if (!_subSceneHandles.TryGetValue(location, out handle))
            {
                LogUtility.Error("Could not unload Scene while not loaded. Scene: {0}", location);
                _handlingScene.Remove(location);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 卸载核心流程（异步）——等待资源系统卸载操作完成并清理登记。
        /// </summary>
        private async UniTask<bool> UnloadSceneInternal(string location, ResourceSceneHandle handle, Action<float> progressCallBack)
        {
            var operation = handle.UnloadAsync();
            if (operation != null)
            {
                while (!operation.IsDone)
                {
                    progressCallBack?.Invoke(operation.Progress);
                    await UniTask.Yield();
                }
            }

            _subScenes.Remove(location);
            _subSceneHandles.Remove(location);
            _handlingScene.Remove(location);

            return true;
        }

        /// <summary>
        /// 卸载核心流程（回调式）。
        /// </summary>
        private async UniTaskVoid UnloadSceneCallbackInternal(string location, ResourceSceneHandle handle, Action callBack, Action<float> progressCallBack)
        {
            await UnloadSceneInternal(location, handle, progressCallBack);
            callBack?.Invoke();
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
