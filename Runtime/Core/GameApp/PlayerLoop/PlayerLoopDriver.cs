using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 剥离 MonoBehaviour 的游戏逻辑驱动器：由 Unity PlayerLoop 直接回调。
    /// </summary>
    /// <remarks>
    /// 订阅存于静态注册表，不挂 GameObject，场景切换 / 宿主销毁不丢失；帧开始先经 <c>GameTime.StartFrame</c> 采样，接口 Handler 与 Action 回调读同一帧快照。 <br />
    /// 零分配契约：三个 Drive 入口与全部 <see cref="IUpdateHandler"/> 实现的热路径不得堆分配（for 循环，禁 LINQ / 闭包 / 字符串拼接）；注册 / 注销在驱动中进入所属阶段延迟缓冲， <br />
    /// 阶段迭代结束后提交。 <br />
    /// 异常分级：开发构建记录后上抛，发布构建隔离续跑；同一订户连续失败达 <see cref="FailureTripThreshold"/> 熔断摘出，核心钩子与关闭广播不参与截断。 <br />
    /// 线程契约：注册表无锁，注册 / 注销仅限主线程（越线程 fail-fast），后台线程先经 <c>MainThreadDispatcher.Post/Send</c> 回主线程。 <br />
    /// 可整体交给 DI 容器（Handler 经构造注入依赖，再由组合根调 <see cref="Register(IUpdateHandler)"/>）。
    /// </remarks>
    internal static partial class PlayerLoopDriver
    {
        #region 常量与标记 [CONSTANTS]

        private const int INITIAL_CAPACITY = 32;

        /// <summary>订阅异常分级策略：开发期 Fatal 后上抛，发布期隔离续跑。</summary>
        /// <remarks><c>const</c> 门控，发布构建裁掉死分支，零运行时成本。</remarks>
        private const bool RETHROW_SUBSCRIBER_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        /// <summary>连续失败熔断默认阈值。与 <c>ServiceWorld.DEFAULT_TICK_TRIP_THRESHOLD</c> 同值（同上，不跨层引用）。</summary>
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

        private static int s_FailureTripThreshold = DEFAULT_FAILURE_TRIP_THRESHOLD;

        /// <summary>连续失败熔断阈值：同一订户在同一阶段连续异常达到该次数即被摘出。</summary>
        /// <remarks>下限钳制为 1（0 或负值会令订户首次失败即熔断）；与 <c>ServiceWorld.TickFailureTripThreshold</c> 同构。</remarks>
        internal static int FailureTripThreshold
        {
            get => s_FailureTripThreshold;
            set => s_FailureTripThreshold = value >= 1 ? value : 1;
        }

        // Unity 生命周期事件表：非帧阶段，低频且无热路径要求，直接用多播委托
        private static Action s_DestroyCallbacks;
        private static Action s_DrawGizmosCallbacks;
        private static Action s_DrawGizmosSelectedCallbacks;
        private static Action<bool> s_ApplicationPauseCallbacks;

        private static bool s_IsDriving;
        private static bool s_IsShutdown = true;

        /// <summary>注册表的主线程 id；0 表示尚未捕获，此时不做主线程判定。</summary>
        internal static int s_MainThreadId;

        /// <summary>
        /// 断言当前处于主线程（注册 / 注销的唯一合法线程）。
        /// </summary>
        /// <remarks>注册表为无锁裸数组，越线程写入不会抛、只会静默丢订阅；断言仅编辑器 / 开发构建参与编译。</remarks>
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







    }
}
