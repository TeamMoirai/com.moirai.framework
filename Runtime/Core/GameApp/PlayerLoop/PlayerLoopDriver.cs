using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos.FrameLoop
{
    /// <summary>
    /// 剥离 MonoBehaviour 的游戏逻辑驱动器：由 Unity PlayerLoop 直接回调。
    /// <para>订阅存储在静态注册表，不挂在任何 GameObject 上——场景切换 / 驱动宿主销毁不会丢失订阅。</para>
    /// <para><b>零分配契约</b>：<see cref="DriveUpdate"/> / <see cref="DriveFixedUpdate"/> / <see cref="DriveLateUpdate"/>
    /// 及所有 <see cref="IUpdateHandler"/> 实现的热路径不得产生堆分配：
    /// 使用 for 循环、禁止 LINQ/闭包/字符串拼接；驱动中注册/注销进入延迟缓冲，迭代结束后统一提交。</para>
    /// <para>DI 集成：将本类或包装服务注册进 VContainer 等容器；Handler 实现经构造注入依赖，
    /// 再由组合根调用 <see cref="Register"/>，驱动与对象创建解耦。</para>
    /// </summary>
    public static class PlayerLoopDriver
    {
        #region 常量与标记 [CONSTANTS]

        private const int INITIAL_CAPACITY = 32;

        private static readonly ProfilerMarker s_UpdateMarker = new ProfilerMarker("PlayerLoopDriver.Update");
        private static readonly ProfilerMarker s_FixedUpdateMarker = new ProfilerMarker("PlayerLoopDriver.FixedUpdate");
        private static readonly ProfilerMarker s_LateUpdateMarker = new ProfilerMarker("PlayerLoopDriver.LateUpdate");

        #endregion

        #region 状态 [STATE]

        private static IUpdateHandler[] s_UpdateHandlers = new IUpdateHandler[INITIAL_CAPACITY];
        private static int s_UpdateCount;

        private static IFixedUpdateHandler[] s_FixedHandlers = new IFixedUpdateHandler[INITIAL_CAPACITY];
        private static int s_FixedCount;

        private static ILateUpdateHandler[] s_LateHandlers = new ILateUpdateHandler[INITIAL_CAPACITY];
        private static int s_LateCount;

        // Action 路径：兼容 GameApp.AddUpdateListener 等回调式 API（调用时零分配）
        private static Action[] s_UpdateCallbacks = new Action[INITIAL_CAPACITY];
        private static int s_UpdateCallbackCount;

        private static Action[] s_FixedCallbacks = new Action[INITIAL_CAPACITY];
        private static int s_FixedCallbackCount;

        private static Action[] s_LateCallbacks = new Action[INITIAL_CAPACITY];
        private static int s_LateCallbackCount;

        private static Action s_DestroyCallbacks;
        private static Action<bool> s_ApplicationPauseCallbacks;
        private static Action<bool> s_ApplicationFocusCallbacks;
        private static Action s_ApplicationQuitCallbacks;

        // 驱动中缓冲（避免迭代中改集合；提交阶段无分配路径上的增删）
        private static readonly List<object> s_PendingInterfaceAdd = new List<object>(INITIAL_CAPACITY);
        private static readonly List<object> s_PendingInterfaceRemove = new List<object>(INITIAL_CAPACITY);
        private static readonly List<Action> s_PendingCallbackAdd = new List<Action>(INITIAL_CAPACITY);
        private static readonly List<Action> s_PendingCallbackRemove = new List<Action>(INITIAL_CAPACITY);

        private static readonly List<IUpdateHandler> s_PriorityUpdateBuffer = new List<IUpdateHandler>(INITIAL_CAPACITY);
        private static readonly List<IFixedUpdateHandler> s_PriorityFixedBuffer = new List<IFixedUpdateHandler>(INITIAL_CAPACITY);
        private static readonly List<ILateUpdateHandler> s_PriorityLateBuffer = new List<ILateUpdateHandler>(INITIAL_CAPACITY);

        private static bool s_IsDriving;
        private static bool s_IsShutdown = true;
        private static bool s_LifecycleHooked;

        /// <summary>驱动器是否已关闭（Shutdown 后注册仍可写入，但 Drive 空转）。</summary>
        public static bool IsShutdown => s_IsShutdown;

        /// <summary>Update 接口 Handler 数量（含延迟缓冲中未提交项之外的已注册项）。</summary>
        public static int UpdateHandlerCount => s_UpdateCount;

        /// <summary>FixedUpdate 接口 Handler 数量。</summary>
        public static int FixedUpdateHandlerCount => s_FixedCount;

        /// <summary>LateUpdate 接口 Handler 数量。</summary>
        public static int LateUpdateHandlerCount => s_LateCount;

        /// <summary>Update Action 回调数量。</summary>
        public static int UpdateCallbackCount => s_UpdateCallbackCount;

        /// <summary>FixedUpdate Action 回调数量。</summary>
        public static int FixedUpdateCallbackCount => s_FixedCallbackCount;

        /// <summary>LateUpdate Action 回调数量。</summary>
        public static int LateUpdateCallbackCount => s_LateCallbackCount;

        #endregion

        #region 初始化 / 关闭 [INIT / SHUTDOWN]

        /// <summary>
        /// 确保 PlayerLoop 已注入、生命周期事件已挂钩、驱动器处于活跃态（幂等）。
        /// </summary>
        public static void Initialize()
        {
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
            destroy?.Invoke();

            ClearHandlers();
            UnhookApplicationLifecycle();

            PlayerLoopInjector.RestoreDefault();
        }

        /// <summary>
        /// 仅清空 Handler/回调订阅，不恢复 PlayerLoop、不触发 Destroy。
        /// <para>域重载 / 退出 Play 时由 SubsystemRegistration 路径调用。</para>
        /// </summary>
        public static void ClearHandlers()
        {
            s_UpdateCount = 0;
            s_FixedCount = 0;
            s_LateCount = 0;
            s_UpdateCallbackCount = 0;
            s_FixedCallbackCount = 0;
            s_LateCallbackCount = 0;

            Array.Clear(s_UpdateHandlers, 0, s_UpdateHandlers.Length);
            Array.Clear(s_FixedHandlers, 0, s_FixedHandlers.Length);
            Array.Clear(s_LateHandlers, 0, s_LateHandlers.Length);
            Array.Clear(s_UpdateCallbacks, 0, s_UpdateCallbacks.Length);
            Array.Clear(s_FixedCallbacks, 0, s_FixedCallbacks.Length);
            Array.Clear(s_LateCallbacks, 0, s_LateCallbacks.Length);

            s_PendingInterfaceAdd.Clear();
            s_PendingInterfaceRemove.Clear();
            s_PendingCallbackAdd.Clear();
            s_PendingCallbackRemove.Clear();
            s_PriorityUpdateBuffer.Clear();
            s_PriorityFixedBuffer.Clear();
            s_PriorityLateBuffer.Clear();

            s_DestroyCallbacks = null;
            s_ApplicationPauseCallbacks = null;
            s_ApplicationFocusCallbacks = null;
            s_ApplicationQuitCallbacks = null;

            s_IsDriving = false;
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
            s_ApplicationQuitCallbacks?.Invoke();
        }

        private static void OnApplicationFocusChanged(bool hasFocus)
        {
            s_ApplicationFocusCallbacks?.Invoke(hasFocus);
        }

        /// <summary>广播 ApplicationPause（由协程宿主 OnApplicationPause 转发）。</summary>
        public static void RaiseApplicationPause(bool pauseStatus)
        {
            s_ApplicationPauseCallbacks?.Invoke(pauseStatus);
        }

        #endregion

        #region 接口注册 [INTERFACE REGISTRATION]

        /// <summary>注册 Update Handler。驱动中调用将延迟到本轮 Drive 结束后提交。</summary>
        public static void Register(IUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving)
            {
                s_PendingInterfaceAdd.Add(handler);
                return;
            }
            RegisterUpdateImmediate(handler);
        }

        /// <summary>注册 FixedUpdate Handler。</summary>
        public static void Register(IFixedUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving)
            {
                s_PendingInterfaceAdd.Add(handler);
                return;
            }
            RegisterFixedImmediate(handler);
        }

        /// <summary>注册 LateUpdate Handler。</summary>
        public static void Register(ILateUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving)
            {
                s_PendingInterfaceAdd.Add(handler);
                return;
            }
            RegisterLateImmediate(handler);
        }

        /// <summary>注销 Update Handler。</summary>
        public static void Unregister(IUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving)
            {
                s_PendingInterfaceRemove.Add(handler);
                return;
            }
            UnregisterUpdateImmediate(handler);
        }

        /// <summary>注销 FixedUpdate Handler。</summary>
        public static void Unregister(IFixedUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving)
            {
                s_PendingInterfaceRemove.Add(handler);
                return;
            }
            UnregisterFixedImmediate(handler);
        }

        /// <summary>注销 LateUpdate Handler。</summary>
        public static void Unregister(ILateUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving)
            {
                s_PendingInterfaceRemove.Add(handler);
                return;
            }
            UnregisterLateImmediate(handler);
        }

        /// <summary>
        /// 注册一个对象到其实现的全部 PlayerLoop 阶段（接口多实现便利入口）。
        /// </summary>
        public static void RegisterAll(object handler)
        {
            if (handler == null) return;
            if (handler is IUpdateHandler u) Register(u);
            if (handler is IFixedUpdateHandler f) Register(f);
            if (handler is ILateUpdateHandler l) Register(l);
        }

        /// <summary>
        /// 从其曾注册的全部 PlayerLoop 阶段注销。
        /// </summary>
        public static void UnregisterAll(object handler)
        {
            if (handler == null) return;
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
            if (s_IsDriving)
            {
                s_PendingCallbackAdd.Add(callback);
                return;
            }
            if (ContainsCallback(s_UpdateCallbacks, s_UpdateCallbackCount, callback)) return;
            EnsureCallbackCapacity(ref s_UpdateCallbacks, s_UpdateCallbackCount);
            s_UpdateCallbacks[s_UpdateCallbackCount++] = callback;
        }

        /// <summary>注册 FixedUpdate 回调。</summary>
        public static void AddFixedUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving)
            {
                s_PendingCallbackAdd.Add(callback);
                return;
            }
            if (ContainsCallback(s_FixedCallbacks, s_FixedCallbackCount, callback)) return;
            EnsureCallbackCapacity(ref s_FixedCallbacks, s_FixedCallbackCount);
            s_FixedCallbacks[s_FixedCallbackCount++] = callback;
        }

        /// <summary>注册 LateUpdate 回调。</summary>
        public static void AddLateUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving)
            {
                s_PendingCallbackAdd.Add(callback);
                return;
            }
            if (ContainsCallback(s_LateCallbacks, s_LateCallbackCount, callback)) return;
            EnsureCallbackCapacity(ref s_LateCallbacks, s_LateCallbackCount);
            s_LateCallbacks[s_LateCallbackCount++] = callback;
        }

        /// <summary>注销 Update 回调。</summary>
        public static void RemoveUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving)
            {
                s_PendingCallbackRemove.Add(callback);
                return;
            }
            RemoveCallback(s_UpdateCallbacks, ref s_UpdateCallbackCount, callback);
        }

        /// <summary>注销 FixedUpdate 回调。</summary>
        public static void RemoveFixedUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving)
            {
                s_PendingCallbackRemove.Add(callback);
                return;
            }
            RemoveCallback(s_FixedCallbacks, ref s_FixedCallbackCount, callback);
        }

        /// <summary>注销 LateUpdate 回调。</summary>
        public static void RemoveLateUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving)
            {
                s_PendingCallbackRemove.Add(callback);
                return;
            }
            RemoveCallback(s_LateCallbacks, ref s_LateCallbackCount, callback);
        }

        /// <summary>注册 Shutdown / Destroy 广播回调。</summary>
        public static void AddDestroyCallback(Action callback)
        {
            if (callback == null) return;
            s_DestroyCallbacks += callback;
        }

        /// <summary>注销 Destroy 广播回调。</summary>
        public static void RemoveDestroyCallback(Action callback)
        {
            if (callback == null) return;
            s_DestroyCallbacks -= callback;
        }

        /// <summary>注册 ApplicationPause 回调。</summary>
        public static void AddApplicationPauseCallback(Action<bool> callback)
        {
            if (callback == null) return;
            s_ApplicationPauseCallbacks += callback;
        }

        /// <summary>注销 ApplicationPause 回调。</summary>
        public static void RemoveApplicationPauseCallback(Action<bool> callback)
        {
            if (callback == null) return;
            s_ApplicationPauseCallbacks -= callback;
        }

        /// <summary>注册 ApplicationFocus 回调。</summary>
        public static void AddApplicationFocusCallback(Action<bool> callback)
        {
            if (callback == null) return;
            s_ApplicationFocusCallbacks += callback;
        }

        /// <summary>注销 ApplicationFocus 回调。</summary>
        public static void RemoveApplicationFocusCallback(Action<bool> callback)
        {
            if (callback == null) return;
            s_ApplicationFocusCallbacks -= callback;
        }

        /// <summary>注册 ApplicationQuit 回调。</summary>
        public static void AddApplicationQuitCallback(Action callback)
        {
            if (callback == null) return;
            s_ApplicationQuitCallbacks += callback;
        }

        /// <summary>注销 ApplicationQuit 回调。</summary>
        public static void RemoveApplicationQuitCallback(Action callback)
        {
            if (callback == null) return;
            s_ApplicationQuitCallbacks -= callback;
        }

        #endregion

        #region 驱动 [DRIVE]

        /// <summary>PlayerLoop Update 阶段入口（由 <see cref="PlayerLoopInjector"/> 调用）。</summary>
        public static void DriveUpdate()
        {
            if (s_IsShutdown) return;

            s_IsDriving = true;
            using (s_UpdateMarker.Auto())
            {
                float dt = GameTime.deltaTime;
                float udt = GameTime.unscaledDeltaTime;

                int handlerCount = s_UpdateCount;
                for (int i = 0; i < handlerCount; i++)
                {
                    IUpdateHandler handler = s_UpdateHandlers[i];
                    if (handler != null) handler.Update(dt, udt);
                }

                int callbackCount = s_UpdateCallbackCount;
                for (int i = 0; i < callbackCount; i++)
                {
                    Action callback = s_UpdateCallbacks[i];
                    callback?.Invoke();
                }
            }
            s_IsDriving = false;
            FlushPending();
        }

        /// <summary>PlayerLoop FixedUpdate 阶段入口。</summary>
        public static void DriveFixedUpdate()
        {
            if (s_IsShutdown) return;

            s_IsDriving = true;
            using (s_FixedUpdateMarker.Auto())
            {
                float fdt = GameTime.fixedDeltaTime;
                float udt = GameTime.unscaledDeltaTime;

                int handlerCount = s_FixedCount;
                for (int i = 0; i < handlerCount; i++)
                {
                    IFixedUpdateHandler handler = s_FixedHandlers[i];
                    if (handler != null) handler.FixedUpdate(fdt, udt);
                }

                int callbackCount = s_FixedCallbackCount;
                for (int i = 0; i < callbackCount; i++)
                {
                    Action callback = s_FixedCallbacks[i];
                    callback?.Invoke();
                }
            }
            s_IsDriving = false;
            FlushPending();
        }

        /// <summary>PlayerLoop LateUpdate（PreLateUpdate 末尾）阶段入口。</summary>
        public static void DriveLateUpdate()
        {
            if (s_IsShutdown) return;

            s_IsDriving = true;
            using (s_LateUpdateMarker.Auto())
            {
                float dt = GameTime.deltaTime;
                float udt = GameTime.unscaledDeltaTime;

                int handlerCount = s_LateCount;
                for (int i = 0; i < handlerCount; i++)
                {
                    ILateUpdateHandler handler = s_LateHandlers[i];
                    if (handler != null) handler.LateUpdate(dt, udt);
                }

                int callbackCount = s_LateCallbackCount;
                for (int i = 0; i < callbackCount; i++)
                {
                    Action callback = s_LateCallbacks[i];
                    callback?.Invoke();
                }
            }
            s_IsDriving = false;
            FlushPending();
        }

        /// <summary>
        /// 将驱动中缓冲的注册变更提交到正式列表。Drive 之间调用；热路径上仅在有 pending 时进入。
        /// </summary>
        private static void FlushPending()
        {
            int interfaceAdd = s_PendingInterfaceAdd.Count;
            int interfaceRemove = s_PendingInterfaceRemove.Count;
            int callbackAdd = s_PendingCallbackAdd.Count;
            int callbackRemove = s_PendingCallbackRemove.Count;
            if (interfaceAdd == 0 && interfaceRemove == 0 && callbackAdd == 0 && callbackRemove == 0)
            {
                return;
            }

            for (int i = 0; i < interfaceRemove; i++)
            {
                object handler = s_PendingInterfaceRemove[i];
                if (handler is IUpdateHandler u) UnregisterUpdateImmediate(u);
                if (handler is IFixedUpdateHandler f) UnregisterFixedImmediate(f);
                if (handler is ILateUpdateHandler l) UnregisterLateImmediate(l);
            }
            s_PendingInterfaceRemove.Clear();

            for (int i = 0; i < interfaceAdd; i++)
            {
                object handler = s_PendingInterfaceAdd[i];
                if (handler is IUpdateHandler u) RegisterUpdateImmediate(u);
                if (handler is IFixedUpdateHandler f) RegisterFixedImmediate(f);
                if (handler is ILateUpdateHandler l) RegisterLateImmediate(l);
            }
            s_PendingInterfaceAdd.Clear();

            for (int i = 0; i < callbackRemove; i++)
            {
                Action cb = s_PendingCallbackRemove[i];
                // 从三个列表中移除（调用方语义是 Remove 指定委托）
                RemoveCallback(s_UpdateCallbacks, ref s_UpdateCallbackCount, cb);
                RemoveCallback(s_FixedCallbacks, ref s_FixedCallbackCount, cb);
                RemoveCallback(s_LateCallbacks, ref s_LateCallbackCount, cb);
            }
            s_PendingCallbackRemove.Clear();

            for (int i = 0; i < callbackAdd; i++)
            {
                Action cb = s_PendingCallbackAdd[i];
                // pending 路径无法区分阶段，仅当尚未存在于任一列表时作为 Update 回调注册
                // —— GameApp 应使用带阶段的 Add*Callback API；此分支兜底防丢
                if (!ContainsCallback(s_UpdateCallbacks, s_UpdateCallbackCount, cb) &&
                    !ContainsCallback(s_FixedCallbacks, s_FixedCallbackCount, cb) &&
                    !ContainsCallback(s_LateCallbacks, s_LateCallbackCount, cb))
                {
                    AddUpdateCallback(cb);
                }
            }
            s_PendingCallbackAdd.Clear();
        }

        #endregion

        #region 立即注册辅助 [IMMEDIATE HELPERS]

        private static void RegisterUpdateImmediate(IUpdateHandler handler)
        {
            if (ContainsHandler(s_UpdateHandlers, s_UpdateCount, handler)) return;

            if (handler is IPlayerLoopPriority)
            {
                s_PriorityUpdateBuffer.Clear();
                for (int i = 0; i < s_UpdateCount; i++)
                {
                    if (s_UpdateHandlers[i] != null) s_PriorityUpdateBuffer.Add(s_UpdateHandlers[i]);
                }
                s_PriorityUpdateBuffer.Add(handler);
                InsertionSortUpdate(s_PriorityUpdateBuffer);
                EnsureHandlerCapacity(ref s_UpdateHandlers, s_PriorityUpdateBuffer.Count);
                s_UpdateCount = s_PriorityUpdateBuffer.Count;
                for (int i = 0; i < s_UpdateCount; i++) s_UpdateHandlers[i] = s_PriorityUpdateBuffer[i];
                s_PriorityUpdateBuffer.Clear();
                return;
            }

            EnsureHandlerCapacity(ref s_UpdateHandlers, s_UpdateCount);
            s_UpdateHandlers[s_UpdateCount++] = handler;
        }

        private static void RegisterFixedImmediate(IFixedUpdateHandler handler)
        {
            if (ContainsHandler(s_FixedHandlers, s_FixedCount, handler)) return;

            if (handler is IPlayerLoopPriority)
            {
                s_PriorityFixedBuffer.Clear();
                for (int i = 0; i < s_FixedCount; i++)
                {
                    if (s_FixedHandlers[i] != null) s_PriorityFixedBuffer.Add(s_FixedHandlers[i]);
                }
                s_PriorityFixedBuffer.Add(handler);
                InsertionSortFixed(s_PriorityFixedBuffer);
                EnsureHandlerCapacity(ref s_FixedHandlers, s_PriorityFixedBuffer.Count);
                s_FixedCount = s_PriorityFixedBuffer.Count;
                for (int i = 0; i < s_FixedCount; i++) s_FixedHandlers[i] = s_PriorityFixedBuffer[i];
                s_PriorityFixedBuffer.Clear();
                return;
            }

            EnsureHandlerCapacity(ref s_FixedHandlers, s_FixedCount);
            s_FixedHandlers[s_FixedCount++] = handler;
        }

        private static void RegisterLateImmediate(ILateUpdateHandler handler)
        {
            if (ContainsHandler(s_LateHandlers, s_LateCount, handler)) return;

            if (handler is IPlayerLoopPriority)
            {
                s_PriorityLateBuffer.Clear();
                for (int i = 0; i < s_LateCount; i++)
                {
                    if (s_LateHandlers[i] != null) s_PriorityLateBuffer.Add(s_LateHandlers[i]);
                }
                s_PriorityLateBuffer.Add(handler);
                InsertionSortLate(s_PriorityLateBuffer);
                EnsureHandlerCapacity(ref s_LateHandlers, s_PriorityLateBuffer.Count);
                s_LateCount = s_PriorityLateBuffer.Count;
                for (int i = 0; i < s_LateCount; i++) s_LateHandlers[i] = s_PriorityLateBuffer[i];
                s_PriorityLateBuffer.Clear();
                return;
            }

            EnsureHandlerCapacity(ref s_LateHandlers, s_LateCount);
            s_LateHandlers[s_LateCount++] = handler;
        }

        private static void UnregisterUpdateImmediate(IUpdateHandler handler)
        {
            for (int i = 0; i < s_UpdateCount; i++)
            {
                if (!ReferenceEquals(s_UpdateHandlers[i], handler)) continue;
                // 尾部前移，保持相对顺序
                for (int j = i; j < s_UpdateCount - 1; j++)
                {
                    s_UpdateHandlers[j] = s_UpdateHandlers[j + 1];
                }
                s_UpdateHandlers[--s_UpdateCount] = null;
                return;
            }
        }

        private static void UnregisterFixedImmediate(IFixedUpdateHandler handler)
        {
            for (int i = 0; i < s_FixedCount; i++)
            {
                if (!ReferenceEquals(s_FixedHandlers[i], handler)) continue;
                for (int j = i; j < s_FixedCount - 1; j++)
                {
                    s_FixedHandlers[j] = s_FixedHandlers[j + 1];
                }
                s_FixedHandlers[--s_FixedCount] = null;
                return;
            }
        }

        private static void UnregisterLateImmediate(ILateUpdateHandler handler)
        {
            for (int i = 0; i < s_LateCount; i++)
            {
                if (!ReferenceEquals(s_LateHandlers[i], handler)) continue;
                for (int j = i; j < s_LateCount - 1; j++)
                {
                    s_LateHandlers[j] = s_LateHandlers[j + 1];
                }
                s_LateHandlers[--s_LateCount] = null;
                return;
            }
        }

        private static void InsertionSortUpdate(List<IUpdateHandler> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                IUpdateHandler key = list[i];
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

        private static void InsertionSortFixed(List<IFixedUpdateHandler> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                IFixedUpdateHandler key = list[i];
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

        private static void InsertionSortLate(List<ILateUpdateHandler> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                ILateUpdateHandler key = list[i];
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

        private static int GetPriority(object handler)
        {
            return handler is IPlayerLoopPriority p ? p.Priority : 0;
        }

        private static void EnsureHandlerCapacity<T>(ref T[] array, int count)
        {
            if (count < array.Length) return;
            int newCapacity = array.Length * 2;
            Array.Resize(ref array, newCapacity);
        }

        private static void EnsureCallbackCapacity(ref Action[] array, int count)
        {
            if (count < array.Length) return;
            int newCapacity = array.Length * 2;
            Array.Resize(ref array, newCapacity);
        }

        private static bool ContainsHandler<T>(T[] array, int count, T handler) where T : class
        {
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(array[i], handler)) return true;
            }
            return false;
        }

        private static bool ContainsCallback(Action[] array, int count, Action callback)
        {
            for (int i = 0; i < count; i++)
            {
                if (array[i] == callback) return true;
            }
            return false;
        }

        private static void RemoveCallback(Action[] array, ref int count, Action callback)
        {
            for (int i = 0; i < count; i++)
            {
                if (array[i] != callback) continue;
                for (int j = i; j < count - 1; j++)
                {
                    array[j] = array[j + 1];
                }
                array[--count] = null;
                return;
            }
        }

        #endregion
    }
}
