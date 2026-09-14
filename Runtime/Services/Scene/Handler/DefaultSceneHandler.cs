using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
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
    /// <para>登记结构：全部登记状态收敛于 <see cref="SceneRegistry"/>（纯决策单元，可独立单测）；
    /// 本类仅负责异步编排、进度/回调边界与日志/异常翻译。句柄只存一处（主场景在途用登记簿在途字段，子场景用子场景表）。</para>
    /// <para>挂起加载契约：底层加载不可中止，挂起场景必须最终 <see cref="UnSuspend"/>；
    /// 等待方取消（<see cref="CancellationToken"/>）只放弃等待，登记与事件由后台续体在加载真正结束时收尾。</para>
    /// <para>由 <see cref="SceneServiceSettings"/> 序列化配置，可替换为自定义场景加载后端。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultSceneHandler : SceneServiceHandler
    {
        /// <summary>
        /// 场景登记簿——主/子场景登记、在途防重入与短名索引的唯一状态源。
        /// </summary>
        [NonSerialized] private readonly SceneRegistry _registry = new SceneRegistry();

        /// <summary>
        /// 当前主场景名称（场景短名）。
        /// </summary>
        public override string CurrentMainSceneName => _registry.CurrentMainSceneName;

        /// <summary>
        /// 已完成加载的子场景资源地址快照（不含加载中的子场景）。
        /// </summary>
        public override IReadOnlyCollection<string> LoadedSubSceneLocations => _registry.SnapshotLoadedSubScenes();

        /// <summary>
        /// 处理器初始化。取当前激活场景作为初始主场景。
        /// </summary>
        protected override void OnInit()
        {
            _registry.CaptureActiveMainScene(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// 处理器关闭，卸载已加载子场景并释放主场景在途/已完成句柄。
        /// <para>登记簿排空保证每个句柄只从唯一登记处取出一次；逐项隔离异常，单个句柄处置失败不中断关闭链。</para>
        /// </summary>
        protected override void OnShutdown()
        {
            // 排空前先取日志上下文（Shutdown 会重置全部字段）
            var mainLoadingLocation = _registry.MainLoadingLocation;
            var mainLocation = _registry.CurrentMainSceneLocation;

            var subScenes = _registry.Shutdown(out var mainLoadingHandle, out var mainHandle);

            // 主场景在途/挂起：不可中止，仅回收引用计数
            ReleaseHandleSafely(mainLoadingHandle, mainLoadingLocation);
            ReleaseHandleSafely(mainHandle, mainLocation);

            for (var i = 0; i < subScenes.Count; i++)
            {
                var entry = subScenes[i];
                try
                {
                    if (entry.State == SceneRegistry.ESubSceneState.Loaded)
                    {
                        entry.Handle.UnloadAsync();
                    }
                    else
                    {
                        entry.Handle.Release();
                    }
                }
                catch (Exception ex)
                {
                    LogUtility.Error("Could not dispose sub scene during shutdown. Scene: {0}, error: {1}", entry.SceneName, ex.Message);
                }
            }
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
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
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
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        public override void LoadScene(string location, string packageName, LoadSceneMode sceneMode,
            bool suspendLoad, uint priority, bool gcCollect, Action<UnityEngine.SceneManagement.Scene> callBack, Action<float> progressCallBack)
        {
            LoadSceneCallbackInternal(location, packageName ?? string.Empty, sceneMode, suspendLoad, priority, gcCollect, callBack, progressCallBack).Forget();
        }

        /// <summary>
        /// 回调式加载包装——复用核心加载流程，无论成败恰好回调一次（回调自身异常被隔离记录）。
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

            if (callBack == null)
            {
                return;
            }

            try
            {
                callBack.Invoke(scene);
            }
            catch (Exception ex)
            {
                LogUtility.Error("Scene load callback threw an exception. Scene: {0}, error: {1}", location, ex.Message);
            }
        }

        /// <summary>
        /// 场景加载核心流程——经 <see cref="ResourceService"/> 走资源系统管线加载场景。
        /// <para>门禁失败、资源后端同步失败、加载错误均抛出 <see cref="GameException"/>；
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

            // —— 门禁判定（纯查询，含防重入与互斥检查）——
            var gate = _registry.CheckBeginLoad(location, sceneMode);
            if (gate != SceneRegistry.ELoadGate.Allow)
            {
                throw CreateLoadException(gate, location);
            }

            _registry.TryMarkOperation(location);

            // —— 发起加载 ——
            ResourceSceneHandle handle;
            try
            {
                handle = ResourceService.LoadSceneAsync(location, sceneMode, suspendLoad, priority, packageName);
            }
            catch (Exception)
            {
                // 资源后端同步失败（如资源包未初始化）：释放在途标记后上抛
                _registry.UnmarkOperation(location);
                throw;
            }

            if (handle == null)
            {
                _registry.UnmarkOperation(location);
                throw new GameException(StringUtility.Format("Could not load scene. Resource service is not ready. Scene: {0}", location));
            }

            // —— 登记（句柄只存一处）——
            if (sceneMode == LoadSceneMode.Single)
            {
                _registry.RegisterMainInFlight(location, handle);
            }
            else
            {
                _registry.RegisterSubInFlight(location, handle);
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
                _registry.AbandonLoad(location, sceneMode);
                _registry.UnmarkOperation(location);
                handle.Release();
                throw;
            }

            return FinalizeSceneLoad(location, handle, sceneMode, gcCollect);
        }

        /// <summary>
        /// 按门禁判定结果构造加载异常。
        /// </summary>
        private static GameException CreateLoadException(SceneRegistry.ELoadGate gate, string location)
        {
            switch (gate)
            {
                case SceneRegistry.ELoadGate.InFlight:
                    return new GameException(StringUtility.Format("Could not load scene while an operation is in flight. Scene: {0}", location));
                case SceneRegistry.ELoadGate.SubAlreadyRegistered:
                    return new GameException(StringUtility.Format("Could not load sub scene while already registered. Scene: {0}", location));
                case SceneRegistry.ELoadGate.MainLoadInFlight:
                    return new GameException(StringUtility.Format("Could not load main scene while another main scene is loading. Scene: {0}", location));
                case SceneRegistry.ELoadGate.RegisteredAsSub:
                    return new GameException(StringUtility.Format("Could not load scene as main while registered as sub scene. Scene: {0}", location));
                case SceneRegistry.ELoadGate.RegisteredAsMain:
                    return new GameException(StringUtility.Format("Could not load scene as sub while registered as main scene. Scene: {0}", location));
                default:
                    return new GameException(StringUtility.Format("Could not load scene. Scene: {0}", location));
            }
        }

        /// <summary>
        /// 等待场景加载句柄完成，支持取消等待；进度回调逐帧回报（异常被隔离记录），成功完成时以 1.0 收尾一次。
        /// </summary>
        private static async UniTask AwaitSceneHandle(ResourceSceneHandle handle, Action<float> progressCallBack, CancellationToken cancellationToken)
        {
            while (!handle.IsDone)
            {
                ReportProgress(progressCallBack, handle.Progress);
                await UniTask.Yield(cancellationToken);
            }

            // 失败不伪报 100% 进度
            if (string.IsNullOrEmpty(handle.Error))
            {
                ReportProgress(progressCallBack, 1f);
            }
        }

        /// <summary>
        /// 隔离回报进度——进度回调为调用方代码，单个回调异常仅记录日志，不中断加载/卸载流程。
        /// </summary>
        private static void ReportProgress(Action<float> progressCallBack, float progress)
        {
            if (progressCallBack == null)
            {
                return;
            }

            try
            {
                progressCallBack.Invoke(progress);
            }
            catch (Exception ex)
            {
                LogUtility.Error("Scene progress callback threw an exception. Error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// 加载完成收尾——登记迁移、主场景句柄切换与事件派发。
        /// <para>加载失败时清理全部登记、释放句柄并抛出 <see cref="GameException"/>。</para>
        /// </summary>
        private UnityEngine.SceneManagement.Scene FinalizeSceneLoad(string location, ResourceSceneHandle handle, LoadSceneMode sceneMode, bool gcCollect)
        {
            _registry.UnmarkOperation(location);

            if (!string.IsNullOrEmpty(handle.Error))
            {
                _registry.AbandonLoad(location, sceneMode);
                handle.Release();
                throw new GameException(StringUtility.Format("Could not load scene. Scene: {0}, error: {1}", location, handle.Error));
            }

            var scene = handle.SceneObject;
            var sceneName = scene.IsValid() && !string.IsNullOrEmpty(scene.name) ? scene.name : location;

            if (sceneMode == LoadSceneMode.Additive)
            {
                var overwrittenLocation = _registry.CompleteSubLoad(location, sceneName);
                if (overwrittenLocation != null)
                {
                    LogUtility.Warning("Scene short name collision. Name: {0}, existing: {1}, new: {2}. Name-based queries will resolve to the new location.",
                        sceneName, overwrittenLocation, location);
                }

                if (sceneName == _registry.CurrentMainSceneName)
                {
                    LogUtility.Warning("Sub scene short name collides with the current main scene. Name: {0}, sub location: {1}", sceneName, location);
                }

                SceneService.InvokeSubSceneLoadedEvent(sceneName);
                return scene;
            }

            if (_registry.TryGetSubLocationByName(sceneName, out var collidingSubLocation))
            {
                LogUtility.Warning("Main scene short name collides with a registered sub scene. Name: {0}, sub location: {1}, main location: {2}",
                    sceneName, collidingSubLocation, location);
            }

            // 主场景在途 → 已完成：新场景已激活，旧场景由引擎卸载，此处释放旧句柄回收底层资源引用计数
            var previousHandle = _registry.CompleteMainLoad(location, sceneName, handle);
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
        /// <para>处理器已关闭（<see cref="OnShutdown"/> 已排空登记簿）或登记被清理时直接返回，放弃过期收尾。</para>
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

            // 在途标记随关闭流程清空——登记簿已排空，收尾不再有意义
            if (!_registry.IsOperationMarked(location))
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

            if (_registry.TryGetLoadedSubScene(location, out var entry))
            {
                return entry.Handle.ActivateScene();
            }

            if ((location == _registry.CurrentMainSceneName || location == _registry.CurrentMainSceneLocation) && _registry.MainSceneHandle != null)
            {
                return _registry.MainSceneHandle.ActivateScene();
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
            if (_registry.MainLoadingHandle != null && location == _registry.MainLoadingLocation)
            {
                return _registry.MainLoadingHandle.UnSuspend();
            }

            // 子场景在途（Loading，含挂起）
            if (_registry.TryGetSubScene(location, out var entry) && entry.State == SceneRegistry.ESubSceneState.Loading)
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
            return _registry.IsMainScene(location);
        }

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。失败返回 <c>false</c> 并保留登记供重试。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
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
        /// 卸载子场景（回调式）。回调契约：卸载发起后无论成败恰好回调一次（参数为是否成功）；
        /// 无效请求（地址未登记、存在在途操作）不发起亦不回调。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="callBack">卸载完成回调（参数为是否卸载成功）。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        public override void Unload(string location, Action<bool> callBack, Action<float> progressCallBack)
        {
            if (!TryBeginUnload(location, out var pending))
            {
                return;
            }

            UnloadSceneCallbackInternal(pending.Location, pending.SceneName, pending.Handle, callBack, progressCallBack).Forget();
        }

        /// <summary>
        /// 卸载前置检查——门禁判定并占用在途标记，拒绝路径输出规范日志。
        /// </summary>
        private bool TryBeginUnload(string requestedLocation, out SceneRegistry.PendingUnload pending)
        {
            var gate = _registry.TryBeginUnload(requestedLocation, out pending);
            switch (gate)
            {
                case SceneRegistry.EUnloadGate.Allow:
                    return true;
                case SceneRegistry.EUnloadGate.NotRegistered:
                    LogUtility.Warning("Unload invalid location:{0}", requestedLocation);
                    return false;
                case SceneRegistry.EUnloadGate.InFlight:
                    LogUtility.Warning("Could not unload scene while an operation is in flight. Scene: {0}", pending.Location);
                    return false;
                default:
                    LogUtility.Warning("Could not unload scene while still loading. Scene: {0}", pending.Location);
                    return false;
            }
        }

        /// <summary>
        /// 卸载核心流程（异步）——等待资源系统卸载操作完成，成功后清理登记并派发事件。
        /// <para>卸载失败或句柄已失效时保留登记（场景仍在场）并返回 <c>false</c> 供重试；在途标记由 finally 保证释放。</para>
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
                    ReportProgress(progressCallBack, operation.Progress);
                    await UniTask.Yield();
                }

                if (!operation.Succeed)
                {
                    LogUtility.Error("Could not unload scene. Scene: {0}, error: {1}", location, operation.Error);
                    return false;
                }

                ReportProgress(progressCallBack, 1f);

                _registry.CompleteUnload(location, sceneName);
                SceneService.InvokeSubSceneUnloadedEvent(sceneName);
                return true;
            }
            finally
            {
                _registry.UnmarkOperation(location);
            }
        }

        /// <summary>
        /// 卸载核心流程（回调式）——发起后无论成败恰好回调一次（参数为是否成功；回调自身异常被隔离记录）。
        /// </summary>
        private async UniTaskVoid UnloadSceneCallbackInternal(string location, string sceneName, ResourceSceneHandle handle, Action<bool> callBack, Action<float> progressCallBack)
        {
            var success = false;
            try
            {
                success = await UnloadSceneInternal(location, sceneName, handle, progressCallBack);
            }
            catch (Exception ex)
            {
                LogUtility.Error("Could not unload scene. Scene: {0}, error: {1}", location, ex.Message);
            }

            if (callBack == null)
            {
                return;
            }

            try
            {
                callBack.Invoke(success);
            }
            catch (Exception ex)
            {
                LogUtility.Error("Scene unload callback threw an exception. Scene: {0}, error: {1}", location, ex.Message);
            }
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
            return _registry.IsContainScene(location);
        }

        #endregion

        /// <summary>
        /// 关闭路径安全释放句柄——逐项隔离异常，不中断关闭链。
        /// </summary>
        private static void ReleaseHandleSafely(ResourceSceneHandle handle, string location)
        {
            if (handle == null)
            {
                return;
            }

            try
            {
                handle.Release();
            }
            catch (Exception ex)
            {
                LogUtility.Error("Could not release scene handle during shutdown. Scene: {0}, error: {1}", location, ex.Message);
            }
        }
    }
}
