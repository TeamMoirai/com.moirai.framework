using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 剥离 MonoBehaviour 的游戏逻辑驱动器：由 Unity PlayerLoop 直接回调。
    /// <para>订阅存储在静态注册表，不挂在任何 GameObject 上——场景切换 / 宿主销毁不会丢失订阅。</para>
    /// <para><b>帧时钟</b>：每个 Drive 阶段入口先调用 <see cref="GameTime.StartFrame"/> 采样，
    /// 再依次驱动接口 Handler 与 Action 回调——两类订阅读到的是同一帧的时间快照。</para>
    /// <para><b>零分配契约</b>：<see cref="DriveUpdate"/> / <see cref="DriveFixedUpdate"/> /
    /// <see cref="DriveLateUpdate"/> 及所有 <see cref="IUpdateHandler"/> 实现的热路径不得产生堆分配：
    /// 使用 for 循环、禁止 LINQ/闭包/字符串拼接。驱动中的注册/注销进入<b>所属阶段各自的</b>延迟缓冲，
    /// 该阶段迭代结束后统一提交。</para>
    /// <para><b>异常处置</b>：订户异常按编译期分级——开发构建记录后上抛（第一时间暴露），
    /// 发布构建隔离续跑（单个订户不截断同阶段其余订户），且同一订户连续失败达
    /// <see cref="FailureTripThreshold"/> 即被熔断摘出。核心钩子与关闭/销毁广播不参与截断，
    /// 详见 <see cref="SetCoreUpdateCallback"/> 与 <see cref="InvokeAllQuarantined"/>。</para>
    /// <para>DI 集成：将本类或包装服务注册进 VContainer 等容器；Handler 实现经构造注入依赖，
    /// 再由组合根调用 <see cref="Register(IUpdateHandler)"/>，驱动与对象创建解耦。</para>
    /// <para><b>线程契约</b>：注册表无锁，注册/注销只允许主线程调用（越线程会 fail-fast 断言，
    /// 而非静默丢订阅）。后台线程需先经 <c>MainThreadDispatcher.Post/Send</c> 回到主线程。</para>
    /// </summary>
    internal static class PlayerLoopDriver
    {
        #region 常量与标记 [CONSTANTS]

        private const int INITIAL_CAPACITY = 32;

        /// <summary>
        /// 订阅异常分级策略：开发期记录后上抛（缺陷第一时间暴露），发布期隔离续跑（单个订户不拖垮整阶段）。
        /// <para>与内核 <c>ServiceScope.RETHROW_TICK_EXCEPTIONS</c> 同一约定。<b>两处需同步修改</b>——
        /// 本类属 <c>Runtime/Core</c>，不引用 <c>Runtime/Services</c> 的常量以免逆向依赖。</para>
        /// <para><c>const</c> 门控：JIT 裁掉死分支，发布构建零运行时成本。</para>
        /// </summary>
        private const bool RETHROW_SUBSCRIBER_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>
        /// 连续失败熔断默认阈值。与 <c>ServiceWorld.DEFAULT_TICK_TRIP_THRESHOLD</c> 同值（同上，不跨层引用）。
        /// </summary>
        private const int DEFAULT_FAILURE_TRIP_THRESHOLD = 300;

        private static readonly ProfilerMarker s_UpdateMarker = new ProfilerMarker("PlayerLoopDriver.Update");
        private static readonly ProfilerMarker s_FixedUpdateMarker = new ProfilerMarker("PlayerLoopDriver.FixedUpdate");
        private static readonly ProfilerMarker s_LateUpdateMarker = new ProfilerMarker("PlayerLoopDriver.LateUpdate");

        #endregion

        #region 状态 [STATE]

        // 每阶段一条独立注册表：接口 Handler 与 Action 回调各一份，延迟缓冲同样按阶段隔离。
        // 阶段方法名各异（Update / FixedUpdate / LateUpdate），故以不可变 invoker 委托参数化——
        // 静态初始化时创建一次，隔离/熔断逻辑因此只需写一份。
        private static readonly HandlerSlot<IUpdateHandler> s_Update =
            new HandlerSlot<IUpdateHandler>((h, a, b) => h.Update(a, b), "Update");
        private static readonly HandlerSlot<IFixedUpdateHandler> s_Fixed =
            new HandlerSlot<IFixedUpdateHandler>((h, a, b) => h.FixedUpdate(a, b), "FixedUpdate");
        private static readonly HandlerSlot<ILateUpdateHandler> s_Late =
            new HandlerSlot<ILateUpdateHandler>((h, a, b) => h.LateUpdate(a, b), "LateUpdate");

        private static readonly CallbackSlot s_UpdateCallback = new CallbackSlot("Update");
        private static readonly CallbackSlot s_FixedCallback = new CallbackSlot("FixedUpdate");
        private static readonly CallbackSlot s_LateCallback = new CallbackSlot("LateUpdate");

        // 核心钩子：先于全部用户订户执行、不参与熔断。框架自身的服务层心跳若被某个项目订户的
        // 连续异常连带摘除，后果是整层停摆且无从恢复，故它与用户订户分表存放。
        private static Action s_CoreUpdate;
        private static Action s_CoreFixedUpdate;
        private static Action s_CoreLateUpdate;

        /// <summary>
        /// 连续失败熔断阈值：同一订户在同一阶段连续异常达到该次数即被摘出该阶段。
        /// <para>与 <c>ServiceWorld.TickFailureTripThreshold</c> 同构，供测试调低以在编辑器下驱动熔断路径
        /// （开发构建会先上抛，非上抛分支在编辑器中不可达）。</para>
        /// </summary>
        internal static int FailureTripThreshold { get; set; } = DEFAULT_FAILURE_TRIP_THRESHOLD;

        // Unity 生命周期事件表：非帧阶段，低频且无热路径要求，直接用多播委托
        private static Action s_DestroyCallbacks;
        private static Action s_DrawGizmosCallbacks;
        private static Action s_DrawGizmosSelectedCallbacks;
        private static Action<bool> s_ApplicationPauseCallbacks;
        private static Action<bool> s_ApplicationFocusCallbacks;
        private static Action s_ApplicationQuitCallbacks;

        private static bool s_IsDriving;
        private static bool s_IsShutdown = true;
        private static bool s_LifecycleHooked;

        /// <summary>
        /// 注册表的主线程归属。0 表示尚未捕获（编辑模式测试、或 SubsystemRegistration 顺序未定），
        /// 此时不判定——与 <see cref="GameServices.EnsureMainThread"/> 同一约定。
        /// </summary>
        private static int s_MainThreadId;

        /// <summary>
        /// 注册/注销只能发生在主线程：注册表是裸数组 + 无锁计数，越线程写入不会抛，
        /// 只会静默丢订阅或让延迟缓冲在提交时读到半更新状态。与 GameServices 同一约定：
        /// 断言仅编辑器 / 开发构建参与编译，发布构建方法体为空、被内联后零开销。
        /// </summary>
        internal static void EnsureMainThread()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Assertions.Assert.IsTrue(
                s_MainThreadId == 0 ||
                System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "PlayerLoopDriver 的注册表只能从主线程读写。" +
                "后台线程请先经 MainThreadDispatcher.Post/Send 回到主线程再注册。");
#endif
        }

        /// <summary>驱动器是否已关闭（Shutdown 后注册仍可写入，但 Drive 空转）。</summary>
        public static bool IsShutdown => s_IsShutdown;

        /// <summary>Update 接口 Handler 数量（不含延迟缓冲中未提交项）。</summary>
        public static int UpdateHandlerCount => s_Update.Count;

        /// <summary>FixedUpdate 接口 Handler 数量。</summary>
        public static int FixedUpdateHandlerCount => s_Fixed.Count;

        /// <summary>LateUpdate 接口 Handler 数量。</summary>
        public static int LateUpdateHandlerCount => s_Late.Count;

        /// <summary>Update Action 回调数量。</summary>
        public static int UpdateCallbackCount => s_UpdateCallback.Count;

        /// <summary>FixedUpdate Action 回调数量。</summary>
        public static int FixedUpdateCallbackCount => s_FixedCallback.Count;

        /// <summary>LateUpdate Action 回调数量。</summary>
        public static int LateUpdateCallbackCount => s_LateCallback.Count;

        #endregion

        #region 初始化 / 关闭 [INIT / SHUTDOWN]

        /// <summary>
        /// 确保 PlayerLoop 已注入、生命周期事件已挂钩、驱动器处于活跃态（幂等）。
        /// </summary>
        public static void Initialize()
        {
            if (s_MainThreadId == 0) s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            PlayerLoopInjector.EnsureInjected();
            HookApplicationLifecycle();
            s_IsShutdown = false;
        }

        /// <summary>
        /// 关闭驱动器：触发 Destroy 订阅、清空全部 Handler/回调、恢复默认 PlayerLoop。
        /// <para>幂等——重复调用安全。编辑器退出 Play 与应用退出均走此路径。</para>
        /// </summary>
        public static void Shutdown()
        {
            if (s_IsShutdown) return;

            s_IsShutdown = true;

            // 先广播 Destroy，再清空订阅（与 GameApp 语义一致）
            Action destroy = s_DestroyCallbacks;
            s_DestroyCallbacks = null;
            InvokeAllQuarantined(destroy, "Destroy");

            ClearHandlers();
            UnhookApplicationLifecycle();

            PlayerLoopInjector.RestoreDefault();
        }

        /// <summary>
        /// 清空全部 Handler / 回调订阅，但不广播 Destroy、不恢复 PlayerLoop。
        /// <para>由 <see cref="Shutdown"/> 调用；域重载下静态字段随域自然复位，故
        /// <see cref="ResetOnDomainReload"/> 有意不调它。做成 internal 是为了让测试能只复位注册表，
        /// 不必连 <see cref="PlayerLoopInjector.RestoreDefault"/> 的全局 PlayerLoop 副作用一起触发。</para>
        /// </summary>
        internal static void ClearHandlers()
        {
            s_Update.Clear();
            s_Fixed.Clear();
            s_Late.Clear();
            s_UpdateCallback.Clear();
            s_FixedCallback.Clear();
            s_LateCallback.Clear();

            s_CoreUpdate = null;
            s_CoreFixedUpdate = null;
            s_CoreLateUpdate = null;

            s_DestroyCallbacks = null;
            s_DrawGizmosCallbacks = null;
            s_DrawGizmosSelectedCallbacks = null;
            s_ApplicationPauseCallbacks = null;
            s_ApplicationFocusCallbacks = null;
            s_ApplicationQuitCallbacks = null;

            s_IsDriving = false;
        }

        /// <summary>
        /// 测试专用：复位注册表并设置活跃位，<b>不</b>触碰 PlayerLoop 注入与 Application 事件。
        /// <para>EditMode 测试不能走 <see cref="Initialize"/>——它会 <c>SetPlayerLoop</c> 改写编辑器全局循环，
        /// 而 <see cref="PlayerLoopInjector.RestoreDefault"/> 在 EditMode 下无从复原（默认循环只在
        /// SubsystemRegistration 捕获）。故此处只切活跃位。</para>
        /// </summary>
        internal static void ResetForTests(bool active)
        {
            ClearHandlers();
            s_IsShutdown = !active;
        }

        /// <summary>
        /// SubsystemRegistration：仅复位驱动开关。
        /// <para>不在此 ClearHandlers——同阶段 <c>RuntimeInitializeOnLoadMethod</c> 顺序未定义，
        /// 若此处清空可能抹掉已先注册的订阅。域重载会自然重置静态字段；
        /// 禁用域重载时由退出 Play 的 <see cref="Shutdown"/> 完成清空。</para>
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload()
        {
            s_IsShutdown = true;
            s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        private static void HookApplicationLifecycle()
        {
            if (s_LifecycleHooked) return;
            s_LifecycleHooked = true;
            Application.quitting += OnApplicationQuit;
            Application.focusChanged += OnApplicationFocusChanged;
        }

        private static void UnhookApplicationLifecycle()
        {
            if (!s_LifecycleHooked) return;
            s_LifecycleHooked = false;
            Application.quitting -= OnApplicationQuit;
            Application.focusChanged -= OnApplicationFocusChanged;
        }

        private static void OnApplicationQuit()
        {
            InvokeAllQuarantined(s_ApplicationQuitCallbacks, "ApplicationQuit");
        }

        private static void OnApplicationFocusChanged(bool hasFocus)
        {
            s_ApplicationFocusCallbacks?.Invoke(hasFocus);
        }

        #endregion

        #region Unity 事件注册表 [UNITY EVENT REGISTRATION]

        /// <summary>注册 Shutdown / Destroy 广播回调。</summary>
        public static void AddDestroyCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DestroyCallbacks += callback;
        }

        /// <summary>注销 Destroy 广播回调。</summary>
        public static void RemoveDestroyCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DestroyCallbacks -= callback;
        }

        /// <summary>注册 OnDrawGizmos 回调（仅编辑器；由 <see cref="GameAppHost"/> 转发驱动）。</summary>
        public static void AddDrawGizmosCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosCallbacks += callback;
        }

        /// <summary>注销 OnDrawGizmos 回调。</summary>
        public static void RemoveDrawGizmosCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosCallbacks -= callback;
        }

        /// <summary>注册 OnDrawGizmosSelected 回调（仅编辑器）。</summary>
        public static void AddDrawGizmosSelectedCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosSelectedCallbacks += callback;
        }

        /// <summary>注销 OnDrawGizmosSelected 回调。</summary>
        public static void RemoveDrawGizmosSelectedCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosSelectedCallbacks -= callback;
        }

        /// <summary>注册 ApplicationPause 回调。</summary>
        public static void AddApplicationPauseCallback(Action<bool> callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationPauseCallbacks += callback;
        }

        /// <summary>注销 ApplicationPause 回调。</summary>
        public static void RemoveApplicationPauseCallback(Action<bool> callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationPauseCallbacks -= callback;
        }

        /// <summary>注册 ApplicationFocus 回调。</summary>
        public static void AddApplicationFocusCallback(Action<bool> callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationFocusCallbacks += callback;
        }

        /// <summary>注销 ApplicationFocus 回调。</summary>
        public static void RemoveApplicationFocusCallback(Action<bool> callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationFocusCallbacks -= callback;
        }

        /// <summary>注册 ApplicationQuit 回调。</summary>
        public static void AddApplicationQuitCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationQuitCallbacks += callback;
        }

        /// <summary>注销 ApplicationQuit 回调。</summary>
        public static void RemoveApplicationQuitCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationQuitCallbacks -= callback;
        }

        /// <summary>广播 OnDrawGizmos（由宿主转发，编辑器专用）。</summary>
        public static void RaiseDrawGizmos()
        {
            s_DrawGizmosCallbacks?.Invoke();
        }

        /// <summary>广播 OnDrawGizmosSelected（由宿主转发，编辑器专用）。</summary>
        public static void RaiseDrawGizmosSelected()
        {
            s_DrawGizmosSelectedCallbacks?.Invoke();
        }

        /// <summary>广播 ApplicationPause（由宿主 OnApplicationPause 转发）。</summary>
        public static void RaiseApplicationPause(bool pauseStatus)
        {
            s_ApplicationPauseCallbacks?.Invoke(pauseStatus);
        }

        /// <summary>
        /// 广播 ApplicationQuit。生产路径由 <see cref="Application.quitting"/> 触发，
        /// internal 是给测试留的接缝——关闭广播无法从测试侧唤起引擎事件。
        /// </summary>
        internal static void RaiseApplicationQuit()
        {
            OnApplicationQuit();
        }

        #endregion

        #region 接口注册 [INTERFACE REGISTRATION]

        /// <summary>注册 Update Handler。驱动中调用将延迟到本阶段 Drive 结束后提交。</summary>
        public static void Register(IUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Update.AddPending(handler);
            else s_Update.Add(handler);
        }

        /// <summary>注册 FixedUpdate Handler。</summary>
        public static void Register(IFixedUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Fixed.AddPending(handler);
            else s_Fixed.Add(handler);
        }

        /// <summary>注册 LateUpdate Handler。</summary>
        public static void Register(ILateUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Late.AddPending(handler);
            else s_Late.Add(handler);
        }

        /// <summary>注销 Update Handler。</summary>
        public static void Unregister(IUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Update.RemovePending(handler);
            else s_Update.Remove(handler);
        }

        /// <summary>注销 FixedUpdate Handler。</summary>
        public static void Unregister(IFixedUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Fixed.RemovePending(handler);
            else s_Fixed.Remove(handler);
        }

        /// <summary>注销 LateUpdate Handler。</summary>
        public static void Unregister(ILateUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Late.RemovePending(handler);
            else s_Late.Remove(handler);
        }

        /// <summary>
        /// 注册一个对象到其实现的全部 PlayerLoop 阶段（接口多实现便利入口）。
        /// </summary>
        public static void RegisterAll(object handler)
        {
            if (handler is IUpdateHandler u) Register(u);
            if (handler is IFixedUpdateHandler f) Register(f);
            if (handler is ILateUpdateHandler l) Register(l);
        }

        /// <summary>
        /// 从其曾注册的全部 PlayerLoop 阶段注销。
        /// </summary>
        public static void UnregisterAll(object handler)
        {
            if (handler is IUpdateHandler u) Unregister(u);
            if (handler is IFixedUpdateHandler f) Unregister(f);
            if (handler is ILateUpdateHandler l) Unregister(l);
        }

        #endregion

        #region Action 注册 [ACTION REGISTRATION]

        /// <summary>注册每帧 Update 回调（同步，不依赖 GameObject / UniTask 延迟）。</summary>
        public static void AddUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_UpdateCallback.AddPending(callback);
            else s_UpdateCallback.Add(callback);
        }

        /// <summary>注册 FixedUpdate 回调。</summary>
        public static void AddFixedUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_FixedCallback.AddPending(callback);
            else s_FixedCallback.Add(callback);
        }

        /// <summary>注册 LateUpdate 回调。</summary>
        public static void AddLateUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_LateCallback.AddPending(callback);
            else s_LateCallback.Add(callback);
        }

        /// <summary>注销 Update 回调。</summary>
        public static void RemoveUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_UpdateCallback.RemovePending(callback);
            else s_UpdateCallback.Remove(callback);
        }

        /// <summary>注销 FixedUpdate 回调。</summary>
        public static void RemoveFixedUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_FixedCallback.RemovePending(callback);
            else s_FixedCallback.Remove(callback);
        }

        /// <summary>注销 LateUpdate 回调。</summary>
        public static void RemoveLateUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_LateCallback.RemovePending(callback);
            else s_LateCallback.Remove(callback);
        }

        #endregion

        #region 核心钩子 [CORE HOOKS]

        /// <summary>
        /// 设置 Update 阶段核心钩子（覆盖式，传 null 清除）。
        /// <para>核心钩子先于本阶段全部用户订户执行，且<b>不参与熔断</b>：组合根心跳
        /// （<c>GameServices.Tick</c>）若因某个项目订户的连续异常被连带摘出，后果是整层服务
        /// 静默停摆且无恢复路径。由 <see cref="GameApp"/> 装配。</para>
        /// </summary>
        internal static void SetCoreUpdateCallback(Action callback)
        {
            EnsureMainThread();
            s_CoreUpdate = callback;
        }

        /// <summary>设置 FixedUpdate 阶段核心钩子。语义同 <see cref="SetCoreUpdateCallback"/>。</summary>
        internal static void SetCoreFixedUpdateCallback(Action callback)
        {
            EnsureMainThread();
            s_CoreFixedUpdate = callback;
        }

        /// <summary>设置 LateUpdate 阶段核心钩子。语义同 <see cref="SetCoreUpdateCallback"/>。</summary>
        internal static void SetCoreLateUpdateCallback(Action callback)
        {
            EnsureMainThread();
            s_CoreLateUpdate = callback;
        }

        /// <summary>调用核心钩子：按分级策略处置异常，但永不熔断。</summary>
        private static void InvokeCore(Action core, string stageName)
        {
            if (core == null) return;

            try
            {
                core();
            }
            catch (Exception exception)
            {
                LogUtility.Error("PlayerLoop {0} core hook threw: {1}", stageName, exception);
                if (RETHROW_SUBSCRIBER_EXCEPTIONS) throw;
            }
        }

        #endregion

        #region 驱动 [DRIVE]

        /// <summary>PlayerLoop Update 阶段入口（由 <see cref="PlayerLoopInjector"/> 调用）。</summary>
        public static void DriveUpdate()
        {
            if (s_IsShutdown) return;

            s_IsDriving = true;
            try
            {
                using (s_UpdateMarker.Auto())
                {
                    GameTime.StartFrame();

                    float dt = GameTime.deltaTime;
                    float udt = GameTime.unscaledDeltaTime;

                    InvokeCore(s_CoreUpdate, "Update");
                    s_Update.Drive(dt, udt);
                    s_UpdateCallback.Drive();
                }
            }
            finally
            {
                // 订阅方抛异常不得卡死 driving 标记，否则后续注册将永久滞留在延迟缓冲
                s_IsDriving = false;
                FlushPending();
            }
        }

        /// <summary>PlayerLoop FixedUpdate 阶段入口。</summary>
        public static void DriveFixedUpdate()
        {
            if (s_IsShutdown) return;

            s_IsDriving = true;
            try
            {
                using (s_FixedUpdateMarker.Auto())
                {
                    GameTime.StartFrame();

                    float fdt = GameTime.fixedDeltaTime;
                    float udt = GameTime.unscaledDeltaTime;

                    InvokeCore(s_CoreFixedUpdate, "FixedUpdate");
                    s_Fixed.Drive(fdt, udt);
                    s_FixedCallback.Drive();
                }
            }
            finally
            {
                s_IsDriving = false;
                FlushPending();
            }
        }

        /// <summary>PlayerLoop LateUpdate（PreLateUpdate 末尾）阶段入口。</summary>
        public static void DriveLateUpdate()
        {
            if (s_IsShutdown) return;

            s_IsDriving = true;
            try
            {
                using (s_LateUpdateMarker.Auto())
                {
                    GameTime.StartFrame();

                    float dt = GameTime.deltaTime;
                    float udt = GameTime.unscaledDeltaTime;

                    InvokeCore(s_CoreLateUpdate, "LateUpdate");
                    s_Late.Drive(dt, udt);
                    s_LateCallback.Drive();
                }
            }
            finally
            {
                s_IsDriving = false;
                FlushPending();
            }
        }

        /// <summary>提交各阶段延迟缓冲。每帧每阶段各一次，无 pending 时仅两次 Count 读。</summary>
        private static void FlushPending()
        {
            s_Update.FlushPending();
            s_Fixed.FlushPending();
            s_Late.FlushPending();
            s_UpdateCallback.FlushPending();
            s_FixedCallback.FlushPending();
            s_LateCallback.FlushPending();
        }

        /// <summary>
        /// 逐项调用多播回调：单项异常不截断其余项。
        /// <para>只用于关闭 / 销毁这类<b>一次性清理广播</b>——它们的职责就是清理，截断等于静默漏掉
        /// 后续每一项的释放动作（存档、句柄、订阅退订）。故开发构建也不上抛，异常按 Error 级带栈记录。</para>
        /// <para><see cref="Delegate.GetInvocationList"/> 每次调用有分配，因此<b>不得</b>用于帧热路径。</para>
        /// </summary>
        private static void InvokeAllQuarantined(Action callbacks, string stageName)
        {
            if (callbacks == null) return;

            Delegate[] invocations = callbacks.GetInvocationList();
            for (int i = 0; i < invocations.Length; i++)
            {
                try
                {
                    ((Action)invocations[i])();
                }
                catch (Exception exception)
                {
                    LogUtility.Error("PlayerLoop {0} callback threw: {1}", stageName, exception);
                }
            }
        }

        #endregion

        #region 阶段注册表 [SLOTS]

        /// <summary>
        /// 单阶段的接口 Handler 注册表：紧凑数组 + 本阶段独立的延迟缓冲 + 本阶段独立的失败计数。
        /// <para>数组恒按有效优先级升序（未实现 <see cref="IPlayerLoopPriority"/> 者计 0），同优先级维持注册序（稳定）；
        /// 末位优先级允许直读追加时走 O(1) 快路，否则整表排序插入。</para>
        /// <para>阶段方法名各异，故由构造期注入不可变 invoker——异常隔离与熔断因此只需实现一份。</para>
        /// </summary>
        private sealed class HandlerSlot<T> where T : class
        {
            private readonly Action<T, float, float> m_Invoker;
            private readonly string m_StageName;
            private T[] m_Handlers = new T[INITIAL_CAPACITY];
            private int m_Count;
            private readonly List<T> m_PendingAdd = new List<T>(INITIAL_CAPACITY);
            private readonly List<T> m_PendingRemove = new List<T>(INITIAL_CAPACITY);
            private readonly List<T> m_SortBuffer = new List<T>(INITIAL_CAPACITY);

            // 连续失败计数懒建：健康路径下 m_HasFailures 恒 false，每订户每帧只多读一个 bool
            private bool m_HasFailures;
            private Dictionary<T, int> m_Failures;

            public HandlerSlot(Action<T, float, float> invoker, string stageName)
            {
                m_Invoker = invoker;
                m_StageName = stageName;
            }

            public int Count => m_Count;

            /// <summary>
            /// 驱动本阶段全部订户：逐个隔离异常，同一订户连续失败达阈值即熔断摘出。
            /// <para>热路径零分配——<c>try</c> 本身不产生堆分配，只有抛出路径走日志与计数。
            /// 保留 <c>null</c> 判定：订户若在迭代中被 <see cref="Clear"/> 清空，本帧余下槽位即为空。</para>
            /// </summary>
            public void Drive(float arg1, float arg2)
            {
                T[] handlers = m_Handlers;
                int count = m_Count;
                // 局部变量阻断编译期可达性折叠——避免 throw 之后的熔断索引补偿触发 CS0162（零运行时差异）
                bool rethrow = RETHROW_SUBSCRIBER_EXCEPTIONS;

                for (int i = 0; i < count; i++)
                {
                    T handler = handlers[i];
                    if (handler == null) continue;

                    try
                    {
                        m_Invoker(handler, arg1, arg2);
                        ResetFailuresIfAny(handler);
                    }
                    catch (Exception exception)
                    {
                        LogUtility.Error("PlayerLoop {0} handler threw: {1}", m_StageName, exception);
                        bool tripped = RecordFailure(handler);
                        if (rethrow) throw;
                        // Remove 保序搬移：后继元素左移一位，故回退索引以免跳过，并收缩本地计数
                        if (tripped)
                        {
                            i--;
                            count--;
                        }
                    }
                }
            }

            /// <summary>记一次失败；达阈值则把该订户摘出本阶段并告警一次。重新注册即完全重置计数。</summary>
            private bool RecordFailure(T handler)
            {
                m_HasFailures = true;
                m_Failures ??= new Dictionary<T, int>(INITIAL_CAPACITY);

                int failures = m_Failures.TryGetValue(handler, out int previous) ? previous + 1 : 1;
                m_Failures[handler] = failures;
                if (failures < FailureTripThreshold) return false;

                Remove(handler);
                m_Failures.Remove(handler);
                LogUtility.Warning(
                    "PlayerLoop {0} handler '{1}' was removed after {2} consecutive failures (threshold {3}).",
                    m_StageName, handler.GetType().FullName, failures, FailureTripThreshold);
                return true;
            }

            private void ResetFailuresIfAny(T handler)
            {
                if (!m_HasFailures) return;

                if (m_Failures != null &&
                    m_Failures.TryGetValue(handler, out int recorded) && recorded != 0)
                {
                    m_Failures[handler] = 0;
                }
            }

            public void Add(T handler)
            {
                EnsureMainThread();
                if (Contains(handler)) return;

                // 尾部追加仅在「追加后仍满足优先级升序」时合法：未实现 IPlayerLoopPriority 者有效优先级为 0，
                // 若末位已是正优先级，直接追加会把它挤到正优先级之后，违背「数字小者先跑」——此时必须走排序插入。
                if (m_Count == 0 || GetPriority(m_Handlers[m_Count - 1]) <= GetPriority(handler))
                {
                    EnsureCapacity(m_Count + 1);
                    m_Handlers[m_Count++] = handler;
                    return;
                }

                InsertByPriority(handler);
            }

            /// <summary>把 <paramref name="handler"/> 并入后按优先级整表稳定排序，写回紧凑数组。</summary>
            private void InsertByPriority(T handler)
            {
                m_SortBuffer.Clear();
                for (int i = 0; i < m_Count; i++) m_SortBuffer.Add(m_Handlers[i]);
                m_SortBuffer.Add(handler);
                SortByPriority(m_SortBuffer);

                EnsureCapacity(m_SortBuffer.Count);
                m_Count = m_SortBuffer.Count;
                for (int i = 0; i < m_Count; i++) m_Handlers[i] = m_SortBuffer[i];
                m_SortBuffer.Clear();
            }

            public void Remove(T handler)
            {
                EnsureMainThread();
                for (int i = 0; i < m_Count; i++)
                {
                    if (!ReferenceEquals(m_Handlers[i], handler)) continue;

                    // 尾部前移，保持相对顺序
                    for (int j = i; j < m_Count - 1; j++) m_Handlers[j] = m_Handlers[j + 1];
                    m_Handlers[--m_Count] = null;
                    return;
                }
            }

            public void AddPending(T handler)
            {
                EnsureMainThread();
                m_PendingAdd.Add(handler);
            }

            public void RemovePending(T handler)
            {
                EnsureMainThread();
                m_PendingRemove.Add(handler);
            }

            public void FlushPending()
            {
                int remove = m_PendingRemove.Count;
                int add = m_PendingAdd.Count;
                if (remove == 0 && add == 0) return;

                // 先注销后注册：同阶段内同帧「移除再添加」按调用序生效
                for (int i = 0; i < remove; i++) Remove(m_PendingRemove[i]);
                m_PendingRemove.Clear();

                for (int i = 0; i < add; i++) Add(m_PendingAdd[i]);
                m_PendingAdd.Clear();
            }

            public void Clear()
            {
                m_Count = 0;
                Array.Clear(m_Handlers, 0, m_Handlers.Length);
                m_PendingAdd.Clear();
                m_PendingRemove.Clear();
                m_SortBuffer.Clear();
                m_HasFailures = false;
                m_Failures = null;
            }

            private bool Contains(T handler)
            {
                for (int i = 0; i < m_Count; i++)
                {
                    if (ReferenceEquals(m_Handlers[i], handler)) return true;
                }
                return false;
            }

            private void EnsureCapacity(int required)
            {
                if (required <= m_Handlers.Length) return;

                int capacity = m_Handlers.Length;
                while (capacity < required) capacity *= 2;
                Array.Resize(ref m_Handlers, capacity);
            }

            private static void SortByPriority(List<T> list)
            {
                // 插入排序：稳定性保证同优先级项维持注册序
                for (int i = 1; i < list.Count; i++)
                {
                    T key = list[i];
                    int keyPriority = GetPriority(key);
                    int j = i - 1;
                    while (j >= 0 && GetPriority(list[j]) > keyPriority)
                    {
                        list[j + 1] = list[j];
                        j--;
                    }
                    list[j + 1] = key;
                }
            }

            private static int GetPriority(T handler)
            {
                return handler is IPlayerLoopPriority p ? p.Priority : 0;
            }
        }

        /// <summary>单阶段的 Action 回调注册表（语义同 <see cref="HandlerSlot{T}"/>，无优先级）。</summary>
        private sealed class CallbackSlot
        {
            private readonly string m_StageName;
            private Action[] m_Callbacks = new Action[INITIAL_CAPACITY];
            private int m_Count;
            private readonly List<Action> m_PendingAdd = new List<Action>(INITIAL_CAPACITY);
            private readonly List<Action> m_PendingRemove = new List<Action>(INITIAL_CAPACITY);

            private bool m_HasFailures;
            private Dictionary<Action, int> m_Failures;

            public CallbackSlot(string stageName)
            {
                m_StageName = stageName;
            }

            public int Count => m_Count;

            /// <summary>驱动本阶段全部回调：隔离与熔断语义同 <see cref="HandlerSlot{T}.Drive"/>。</summary>
            public void Drive()
            {
                Action[] callbacks = m_Callbacks;
                int count = m_Count;
                bool rethrow = RETHROW_SUBSCRIBER_EXCEPTIONS;

                for (int i = 0; i < count; i++)
                {
                    Action callback = callbacks[i];
                    if (callback == null) continue;

                    try
                    {
                        callback();
                        ResetFailuresIfAny(callback);
                    }
                    catch (Exception exception)
                    {
                        LogUtility.Error("PlayerLoop {0} callback threw: {1}", m_StageName, exception);
                        bool tripped = RecordFailure(callback);
                        if (rethrow) throw;
                        if (tripped)
                        {
                            i--;
                            count--;
                        }
                    }
                }
            }

            private bool RecordFailure(Action callback)
            {
                m_HasFailures = true;
                m_Failures ??= new Dictionary<Action, int>(INITIAL_CAPACITY);

                int failures = m_Failures.TryGetValue(callback, out int previous) ? previous + 1 : 1;
                m_Failures[callback] = failures;
                if (failures < FailureTripThreshold) return false;

                Remove(callback);
                m_Failures.Remove(callback);
                LogUtility.Warning(
                    "PlayerLoop {0} callback '{1}.{2}' was removed after {3} consecutive failures (threshold {4}).",
                    m_StageName, callback.Method.DeclaringType, callback.Method.Name,
                    failures, FailureTripThreshold);
                return true;
            }

            private void ResetFailuresIfAny(Action callback)
            {
                if (!m_HasFailures) return;

                if (m_Failures != null &&
                    m_Failures.TryGetValue(callback, out int recorded) && recorded != 0)
                {
                    m_Failures[callback] = 0;
                }
            }

            public void Add(Action callback)
            {
                EnsureMainThread();
                if (Contains(callback)) return;

                EnsureCapacity(m_Count + 1);
                m_Callbacks[m_Count++] = callback;
            }

            public void Remove(Action callback)
            {
                EnsureMainThread();
                for (int i = 0; i < m_Count; i++)
                {
                    if (m_Callbacks[i] != callback) continue;

                    for (int j = i; j < m_Count - 1; j++) m_Callbacks[j] = m_Callbacks[j + 1];
                    m_Callbacks[--m_Count] = null;
                    return;
                }
            }

            public void AddPending(Action callback)
            {
                EnsureMainThread();
                m_PendingAdd.Add(callback);
            }

            public void RemovePending(Action callback)
            {
                EnsureMainThread();
                m_PendingRemove.Add(callback);
            }

            public void FlushPending()
            {
                int remove = m_PendingRemove.Count;
                int add = m_PendingAdd.Count;
                if (remove == 0 && add == 0) return;

                for (int i = 0; i < remove; i++) Remove(m_PendingRemove[i]);
                m_PendingRemove.Clear();

                for (int i = 0; i < add; i++) Add(m_PendingAdd[i]);
                m_PendingAdd.Clear();
            }

            public void Clear()
            {
                m_Count = 0;
                Array.Clear(m_Callbacks, 0, m_Callbacks.Length);
                m_PendingAdd.Clear();
                m_PendingRemove.Clear();
                m_HasFailures = false;
                m_Failures = null;
            }

            private bool Contains(Action callback)
            {
                for (int i = 0; i < m_Count; i++)
                {
                    if (m_Callbacks[i] == callback) return true;
                }
                return false;
            }

            private void EnsureCapacity(int required)
            {
                if (required <= m_Callbacks.Length) return;

                int capacity = m_Callbacks.Length;
                while (capacity < required) capacity *= 2;
                Array.Resize(ref m_Callbacks, capacity);
            }
        }

        #endregion
    }
}
