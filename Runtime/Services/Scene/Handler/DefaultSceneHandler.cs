using System;
using System.Collections.Generic;
using System.Threading;
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
    /// <para>标识约定：内部登记以资源地址（location）为键，同时维护场景短名（<see cref="UnityEngine.SceneManagement.Scene.name"/>）反向索引；
    /// 查询/激活/卸载接口同时接受资源地址与场景短名，<see cref="CurrentMainSceneName"/> 与生命周期事件统一使用场景短名。
    /// 场景短名须尽量全局唯一——碰撞时后注册者覆盖反向索引并打 Warning，按名查询可能解析到错误对象。</para>
    /// <para>登记结构：主场景在途用 <see cref="_mainSceneLoadingHandle"/>（完成后迁入 <see cref="_mainSceneHandle"/>）；
    /// 子场景一律登记于 <see cref="_subScenes"/>（<see cref="ESubSceneState.Loading"/> → <see cref="ESubSceneState.Loaded"/>），句柄只存一处。</para>
    /// <para>挂起加载契约：底层加载不可中止，挂起场景必须最终 <see cref="UnSuspend"/>；
    /// 等待方取消（<see cref="CancellationToken"/>）只放弃等待，登记与事件由后台续体在加载真正结束时收尾。</para>
    /// <para>由 <see cref="SceneServiceSettings"/> 序列化配置，可替换为自定义场景加载后端。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultSceneHandler : SceneServiceHandler
    {
        /// <summary>
        /// 子场景登记状态。
        /// </summary>
        private enum ESubSceneState : byte
        {
            /// <summary>已发起加载（可能挂起待激活），尚未完成。</summary>
            Loading = 0,

            /// <summary>加载完成，可激活与卸载。</summary>
            Loaded = 1,
        }

        /// <summary>
        /// 子场景登记项——句柄、归一化场景短名与登记状态。
        /// </summary>
        private readonly struct SubSceneEntry
        {
            /// <summary>资源系统场景句柄。</summary>
            public readonly ResourceSceneHandle Handle;

            /// <summary>归一化场景短名（加载完成前为空串）。</summary>
            public readonly string SceneName;

            /// <summary>登记状态。</summary>
            public readonly ESubSceneState State;

            /// <summary>
            /// 创建子场景登记项。
            /// </summary>
            public SubSceneEntry(ResourceSceneHandle handle, string sceneName, ESubSceneState state)
            {
                Handle = handle;
                SceneName = sceneName;
                State = state;
            }
        }

        /// <summary>
        /// 待执行卸载的解析结果——规范化登记地址、场景短名与句柄。
        /// </summary>
        private readonly struct PendingUnload
        {
            /// <summary>子场景登记地址。</summary>
            public readonly string Location;

            /// <summary>归一化场景短名。</summary>
            public readonly string SceneName;

            /// <summary>资源系统场景句柄。</summary>
            public readonly ResourceSceneHandle Handle;

            /// <summary>
            /// 创建待执行卸载解析结果。
            /// </summary>
            public PendingUnload(string location, string sceneName, ResourceSceneHandle handle)
            {
                Location = location;
                SceneName = sceneName;
                Handle = handle;
            }
        }

        /// <summary>
        /// 当前主场景短名（<see cref="Scene.name"/> 归一化）。
        /// </summary>
        [NonSerialized] private string _currentMainSceneName = string.Empty;

        /// <summary>
        /// 当前主场景资源地址（启动场景未经本服务加载时为空串）。
        /// </summary>
        [NonSerialized] private string _currentMainSceneLocation = string.Empty;

        /// <summary>
        /// 当前主场景句柄（Single 模式加载完成，被替换时释放引用计数）。
        /// </summary>
        [NonSerialized] private ResourceSceneHandle _mainSceneHandle;

        /// <summary>
        /// 主场景在途加载句柄（Single 发起后、收尾前；含挂起待激活）。非空即表示主场景加载互斥中。
        /// </summary>
        [NonSerialized] private ResourceSceneHandle _mainSceneLoadingHandle;

        /// <summary>
        /// 主场景在途加载的资源地址（供 <see cref="UnSuspend"/> 按地址命中）。
        /// </summary>
        [NonSerialized] private string _mainSceneLoadingLocation = string.Empty;

        /// <summary>
        /// 已登记子场景（location → 登记项）。前置登记于加载发起时，Loading/Loaded 表达子场景生命周期；句柄只存于此处。
        /// </summary>
        [NonSerialized] private readonly Dictionary<string, SubSceneEntry> _subScenes = new Dictionary<string, SubSceneEntry>();

        /// <summary>
        /// 子场景短名反向索引（<see cref="Scene.name"/> → location），加载完成时登记。
        /// </summary>
        [NonSerialized] private readonly Dictionary<string, string> _subSceneNameIndex = new Dictionary<string, string>();

        /// <summary>
        /// 在途操作防重入标记（location 级，覆盖加载与卸载）。
        /// </summary>
        [NonSerialized] private readonly HashSet<string> _handlingScene = new HashSet<string>();

        /// <summary>
        /// 当前主场景名称（场景短名）。
        /// </summary>
        public override string CurrentMainSceneName => _currentMainSceneName;

        /// <summary>
        /// 已完成加载的子场景资源地址快照（不含加载中的子场景）。
        /// </summary>
        public override IReadOnlyCollection<string> LoadedSubSceneLocations
        {
            get
            {
                var result = new List<string>(_subScenes.Count);
                foreach (var pair in _subScenes)
                {
                    if (pair.Value.State == ESubSceneState.Loaded)
                    {
                        result.Add(pair.Key);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 处理器初始化。取当前激活场景作为初始主场景（编辑器下启动场景可能不在 Build Settings，
        /// <c>GetSceneByBuildIndex(0)</c> 会得到无效场景）。
        /// </summary>
        protected override void OnInit()
        {
            var activeScene = SceneManager.GetActiveScene();
            _currentMainSceneName = activeScene.IsValid() ? activeScene.name : string.Empty;
        }

        /// <summary>
        /// 处理器关闭，卸载已加载子场景并释放主场景在途/已完成句柄。
        /// <para>每个句柄只从其唯一登记处释放一次：主场景在途用 <see cref="_mainSceneLoadingHandle"/>，子场景用 <see cref="_subScenes"/>。</para>
        /// </summary>
        protected override void OnShutdown()
        {
            // 主场景在途/挂起：不可中止，仅回收引用计数
            _mainSceneLoadingHandle?.Release();
            ClearMainSceneLoading();

            foreach (var pair in _subScenes)
            {
                if (pair.Value.State == ESubSceneState.Loaded)
                {
                    pair.Value.Handle.UnloadAsync();
                }
                else
                {
                    pair.Value.Handle.Release();
                }
            }

            _subScenes.Clear();
            _subSceneNameIndex.Clear();
            _handlingScene.Clear();
            _mainSceneHandle?.Release();
            _mainSceneHandle = null;
            _currentMainSceneName = string.Empty;
            _currentMainSceneLocation = string.Empty;
        }

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 异步加载场景。加载失败抛出 <see cref="GameException"/>。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="progressCallBack">进度回调（必以 1.0 收尾一次）。</param>
        /// <param name="packageName">资源包名称（空串使用默认包）。</param>
        /// <param name="cancellationToken">取消令牌——放弃等待语义，不中止底层加载。</param>
        /// <returns>加载完成的场景。</returns>
        public override UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority,
            bool gcCollect, Action<float> progressCallBack, string packageName, CancellationToken cancellationToken)
        {
            // 直通返回内部任务，避免外层 async 多分配一个状态机
            return LoadSceneInternal(location, packageName ?? string.Empty, sceneMode, suspendLoad, priority, gcCollect, progressCallBack, cancellationToken);
        }

        /// <summary>
        /// 同步发起场景加载（回调式）。
        /// <para>回调契约：无论成败恰好回调一次；失败时以默认场景回调，调用方须检查 <see cref="UnityEngine.SceneManagement.Scene.IsValid"/>。</para>
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="callBack">加载完成回调。</param>
        /// <param name="progressCallBack">进度回调（必以 1.0 收尾一次）。</param>
        public override void LoadScene(string location, string packageName, LoadSceneMode sceneMode,
            bool suspendLoad, uint priority, bool gcCollect, Action<UnityEngine.SceneManagement.Scene> callBack, Action<float> progressCallBack)
        {
            LoadSceneCallbackInternal(location, packageName ?? string.Empty, sceneMode, suspendLoad, priority, gcCollect, callBack, progressCallBack).Forget();
        }

        /// <summary>
        /// 回调式加载包装——复用核心加载流程，无论成败恰好回调一次。
        /// </summary>
        private async UniTaskVoid LoadSceneCallbackInternal(string location, string packageName, LoadSceneMode sceneMode,
            bool suspendLoad, uint priority, bool gcCollect, Action<UnityEngine.SceneManagement.Scene> callBack, Action<float> progressCallBack)
        {
            var scene = default(UnityEngine.SceneManagement.Scene);
            try
            {
                scene = await LoadSceneInternal(location, packageName, sceneMode, suspendLoad, priority, gcCollect, progressCallBack, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogUtility.Error("Could not load scene. Scene: {0}, error: {1}", location, ex.Message);
            }

            callBack?.Invoke(scene);
        }

        /// <summary>
        /// 场景加载核心流程——经 <see cref="ResourceService"/> 走资源系统管线加载场景。
        /// <para>防重入与互斥失败、资源后端同步失败、加载错误均抛出 <see cref="GameException"/>；
        /// 等待方取消时由 <see cref="FinalizeLoadDetached"/> 后台收尾后重抛 <see cref="OperationCanceledException"/>。</para>
        /// </summary>
        private async UniTask<UnityEngine.SceneManagement.Scene> LoadSceneInternal(string location, string packageName, LoadSceneMode sceneMode,
            bool suspendLoad, uint priority, bool gcCollect, Action<float> progressCallBack, CancellationToken cancellationToken)
        {
            // —— 入参校验 ——
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Could not load scene. Location is null or empty.");
            }

            // —— 防重入与互斥检查 ——
            if (!_handlingScene.Add(location))
            {
                throw new GameException($"Could not load scene while an operation is in flight. Scene: {location}");
            }

            if (sceneMode == LoadSceneMode.Additive && _subScenes.ContainsKey(location))
            {
                _handlingScene.Remove(location);
                throw new GameException($"Could not load sub scene while already registered. Scene: {location}");
            }

            if (sceneMode == LoadSceneMode.Single && _mainSceneLoadingHandle != null)
            {
                _handlingScene.Remove(location);
                throw new GameException($"Could not load main scene while another main scene is loading. Scene: {location}");
            }

            // —— 发起加载 ——
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
                throw new GameException($"Could not load scene. Resource service is not ready. Scene: {location}");
            }

            // —— 登记（句柄只存一处）——
            if (sceneMode == LoadSceneMode.Single)
            {
                _mainSceneLoadingHandle = handle;
                _mainSceneLoadingLocation = location;
            }
            else
            {
                // 前置登记——挂起加载的场景在 UnSuspend 之后才会完成加载
                _subScenes[location] = new SubSceneEntry(handle, string.Empty, ESubSceneState.Loading);
            }

            // —— 等待完成 ——
            try
            {
                await AwaitSceneHandle(handle, progressCallBack, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 放弃等待：底层加载不可中止，登记与事件由后台续体在加载真正结束时收尾
                FinalizeLoadDetached(location, handle, sceneMode, gcCollect).Forget();
                throw;
            }
            catch (Exception)
            {
                // 真实异常：登记作废、句柄释放，后端状态视为不可用（fail fast）
                AbandonLoadRegistration(location, sceneMode);
                _handlingScene.Remove(location);
                handle.Release();
                throw;
            }

            return FinalizeSceneLoad(location, handle, sceneMode, gcCollect);
        }

        /// <summary>
        /// 放弃加载登记——主场景清空在途字段，子场景移除 Loading 项。
        /// </summary>
        private void AbandonLoadRegistration(string location, LoadSceneMode sceneMode)
        {
            if (sceneMode == LoadSceneMode.Single)
            {
                // 仅当仍指向本次在途加载时清空，避免误清后续主场景加载
                if (_mainSceneLoadingHandle != null && location == _mainSceneLoadingLocation)
                {
                    ClearMainSceneLoading();
                }
            }
            else
            {
                _subScenes.Remove(location);
            }
        }

        /// <summary>
        /// 清空主场景在途加载字段（互斥标记由句柄是否为空隐含表达）。
        /// </summary>
        private void ClearMainSceneLoading()
        {
            _mainSceneLoadingHandle = null;
            _mainSceneLoadingLocation = string.Empty;
        }

        /// <summary>
        /// 等待场景加载句柄完成，可选进度回调（必以 1.0 收尾一次），支持取消等待。
        /// </summary>
        private static async UniTask AwaitSceneHandle(ResourceSceneHandle handle, Action<float> progressCallBack, CancellationToken cancellationToken)
        {
            while (!handle.IsDone)
            {
                progressCallBack?.Invoke(handle.Progress);
                await UniTask.Yield(cancellationToken);
            }

            progressCallBack?.Invoke(1f);
        }

        /// <summary>
        /// 加载完成收尾——登记迁移、主场景句柄切换与事件派发。
        /// <para>加载失败时清理全部登记、释放句柄并抛出 <see cref="GameException"/>。</para>
        /// </summary>
        private UnityEngine.SceneManagement.Scene FinalizeSceneLoad(string location, ResourceSceneHandle handle, LoadSceneMode sceneMode, bool gcCollect)
        {
            _handlingScene.Remove(location);

            if (!string.IsNullOrEmpty(handle.Error))
            {
                AbandonLoadRegistration(location, sceneMode);
                handle.Release();
                throw new GameException($"Could not load scene. Scene: {location}, error: {handle.Error}");
            }

            var scene = handle.SceneObject;
            var sceneName = scene.IsValid() && !string.IsNullOrEmpty(scene.name) ? scene.name : location;

            if (sceneMode == LoadSceneMode.Additive)
            {
                RegisterSubSceneNameIndex(location, sceneName);
                _subScenes[location] = new SubSceneEntry(handle, sceneName, ESubSceneState.Loaded);
                SceneService.InvokeSubSceneLoadedEvent(sceneName);
                return scene;
            }

            // 主场景在途 → 已完成：先清在途再迁入 _mainSceneHandle
            AbandonLoadRegistration(location, LoadSceneMode.Single);

            if (_subSceneNameIndex.TryGetValue(sceneName, out var collidingSubLocation))
            {
                LogUtility.Warning("Main scene short name collides with a registered sub scene. Name: {0}, sub location: {1}, main location: {2}",
                    sceneName, collidingSubLocation, location);
            }

            var previousHandle = _mainSceneHandle;
            _mainSceneHandle = handle;
            _currentMainSceneLocation = location;
            _currentMainSceneName = sceneName;
            previousHandle?.Release();

#if UNITY_EDITOR && EditorFixedMaterialShader
            MaterialUtility.WaitGetRootGameObjects(scene).Forget();
#endif

            SceneService.InvokeMainSceneChangedEvent(sceneName);

            ResourceService.ForceUnloadUnusedAssets(gcCollect);

            return scene;
        }

        /// <summary>
        /// 放弃等待后的后台收尾续体——轮询至加载真正完成后执行与同步路径一致的收尾逻辑。
        /// <para>处理器已关闭（句柄被 <see cref="OnShutdown"/> 释放后 <c>IsDone</c> 恒为真）时直接返回。</para>
        /// </summary>
        private async UniTaskVoid FinalizeLoadDetached(string location, ResourceSceneHandle handle, LoadSceneMode sceneMode, bool gcCollect)
        {
            while (!handle.IsDone)
            {
                await UniTask.Yield();
            }

            if (!IsInitialized)
            {
                return;
            }

            try
            {
                FinalizeSceneLoad(location, handle, sceneMode, gcCollect);
            }
            catch (Exception ex)
            {
                LogUtility.Error("Could not finalize detached scene load. Scene: {0}, error: {1}", location, ex.Message);
            }
        }

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景（设为当前活动场景）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否激活成功。</returns>
        public override bool ActivateScene(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return false;
            }

            var subLocation = ResolveSubSceneLocation(location);
            if (subLocation != null && _subScenes.TryGetValue(subLocation, out var entry) && entry.State == ESubSceneState.Loaded)
            {
                return entry.Handle.ActivateScene();
            }

            if ((location == _currentMainSceneName || location == _currentMainSceneLocation) && _mainSceneHandle != null)
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
        /// 取消挂起。仅接受资源地址（挂起场景尚未产生场景短名）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        public override bool UnSuspend(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                LogUtility.Warning("UnSuspend invalid location:{0}", location);
                return false;
            }

            // 主场景在途（含挂起）
            if (_mainSceneLoadingHandle != null && location == _mainSceneLoadingLocation)
            {
                return _mainSceneLoadingHandle.UnSuspend();
            }

            // 子场景在途（Loading，含挂起）
            if (_subScenes.TryGetValue(location, out var entry) && entry.State == ESubSceneState.Loading)
            {
                return entry.Handle.UnSuspend();
            }

            LogUtility.Warning("UnSuspend invalid location:{0}", location);
            return false;
        }

        /// <summary>
        /// 判断指定场景是否为当前主场景（身份判断，不含激活状态）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否为主场景。</returns>
        public override bool IsMainScene(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return false;
            }

            return location == _currentMainSceneName || location == _currentMainSceneLocation;
        }

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。失败返回 <c>false</c> 并保留登记供重试。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="progressCallBack">进度回调（必以 1.0 收尾一次）。</param>
        /// <returns>是否卸载成功。</returns>
        public override UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack)
        {
            if (!TryBeginUnload(location, out var pending))
            {
                return UniTask.FromResult(false);
            }

            return UnloadSceneInternal(pending.Location, pending.SceneName, pending.Handle, progressCallBack);
        }

        /// <summary>
        /// 卸载子场景（回调式）。回调契约：卸载发起后无论成败恰好回调一次；无效请求（地址未登记、存在在途操作）不发起亦不回调。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调（必以 1.0 收尾一次）。</param>
        public override void Unload(string location, Action callBack, Action<float> progressCallBack)
        {
            if (!TryBeginUnload(location, out var pending))
            {
                return;
            }

            UnloadSceneCallbackInternal(pending.Location, pending.SceneName, pending.Handle, callBack, progressCallBack).Forget();
        }

        /// <summary>
        /// 卸载前置检查——将入参解析为子场景登记项，校验已加载且无在途操作，通过后占用防重入标记。
        /// </summary>
        private bool TryBeginUnload(string requestedLocation, out PendingUnload pending)
        {
            pending = default;

            var location = ResolveSubSceneLocation(requestedLocation);
            if (location == null || !_subScenes.TryGetValue(location, out var entry))
            {
                LogUtility.Warning("Unload invalid location:{0}", requestedLocation);
                return false;
            }

            if (!_handlingScene.Add(location))
            {
                LogUtility.Warning("Could not unload scene while an operation is in flight. Scene: {0}", location);
                return false;
            }

            if (entry.State != ESubSceneState.Loaded)
            {
                _handlingScene.Remove(location);
                LogUtility.Warning("Could not unload scene while still loading. Scene: {0}", location);
                return false;
            }

            pending = new PendingUnload(location, entry.SceneName, entry.Handle);
            return true;
        }

        /// <summary>
        /// 卸载核心流程（异步）——等待资源系统卸载操作完成，成功后清理登记并派发事件。
        /// <para>卸载失败或句柄已失效时保留登记（场景仍在场）并返回 <c>false</c> 供重试；<c>_handlingScene</c> 由 finally 保证释放。</para>
        /// </summary>
        private async UniTask<bool> UnloadSceneInternal(string location, string sceneName, ResourceSceneHandle handle, Action<float> progressCallBack)
        {
            try
            {
                IResourceOperation operation = null;
                try
                {
                    operation = handle.UnloadAsync();
                }
                catch (Exception ex)
                {
                    LogUtility.Error("Could not unload scene. Scene: {0}, error: {1}", location, ex.Message);
                    return false;
                }

                // Loaded 登记项的句柄应当可卸载；null 表示句柄已失效（如适配器发起时已置空），按失败处理，不得误报成功
                if (operation == null)
                {
                    LogUtility.Error("Could not unload scene. Scene handle is already invalid. Scene: {0}", location);
                    return false;
                }

                while (!operation.IsDone)
                {
                    progressCallBack?.Invoke(operation.Progress);
                    await UniTask.Yield();
                }

                progressCallBack?.Invoke(1f);

                if (!operation.Succeed)
                {
                    LogUtility.Error("Could not unload scene. Scene: {0}, error: {1}", location, operation.Error);
                    return false;
                }

                _subScenes.Remove(location);
                if (!string.IsNullOrEmpty(sceneName) &&
                    _subSceneNameIndex.TryGetValue(sceneName, out var mappedLocation) && mappedLocation == location)
                {
                    // 短名碰撞时后注册者可能已覆盖索引，仅移除仍指向本地址的项
                    _subSceneNameIndex.Remove(sceneName);
                }

                SceneService.InvokeSubSceneUnloadedEvent(sceneName);
                return true;
            }
            finally
            {
                _handlingScene.Remove(location);
            }
        }

        /// <summary>
        /// 卸载核心流程（回调式）——发起后无论成败恰好回调一次。
        /// </summary>
        private async UniTaskVoid UnloadSceneCallbackInternal(string location, string sceneName, ResourceSceneHandle handle, Action callBack, Action<float> progressCallBack)
        {
            try
            {
                await UnloadSceneInternal(location, sceneName, handle, progressCallBack);
            }
            catch (Exception ex)
            {
                LogUtility.Error("Could not unload scene. Scene: {0}, error: {1}", location, ex.Message);
            }

            callBack?.Invoke();
        }

        #endregion

        #region 场景查询 [SCENE QUERY]

        /// <summary>
        /// 查询场景是否已登记（主场景含启动场景，子场景含加载中未完成的挂起加载）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否已登记。</returns>
        public override bool IsContainScene(string location)
        {
            if (string.IsNullOrEmpty(location))
            {
                return false;
            }

            if (location == _currentMainSceneName || location == _currentMainSceneLocation)
            {
                return true;
            }

            // 主场景在途加载也视为已登记
            if (_mainSceneLoadingHandle != null && location == _mainSceneLoadingLocation)
            {
                return true;
            }

            return _subScenes.ContainsKey(location) || _subSceneNameIndex.ContainsKey(location);
        }

        #endregion

        /// <summary>
        /// 登记子场景短名反向索引。同名不同地址时后注册者覆盖，并打 Warning 提示按名查询可能解析错误。
        /// </summary>
        private void RegisterSubSceneNameIndex(string location, string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            if (_subSceneNameIndex.TryGetValue(sceneName, out var existingLocation) && existingLocation != location)
            {
                LogUtility.Warning("Scene short name collision. Name: {0}, existing: {1}, new: {2}. Name-based queries will resolve to the new location.",
                    sceneName, existingLocation, location);
            }

            _subSceneNameIndex[sceneName] = location;
        }

        /// <summary>
        /// 将入参解析为子场景登记地址——直接命中登记键，否则经场景短名反向索引解析。
        /// </summary>
        /// <param name="locationOrName">资源地址或场景短名。</param>
        /// <returns>登记地址；未命中返回 <c>null</c>。</returns>
        private string ResolveSubSceneLocation(string locationOrName)
        {
            if (string.IsNullOrEmpty(locationOrName))
            {
                return null;
            }

            if (_subScenes.ContainsKey(locationOrName))
            {
                return locationOrName;
            }

            return _subSceneNameIndex.TryGetValue(locationOrName, out var location) ? location : null;
        }
    }
}
