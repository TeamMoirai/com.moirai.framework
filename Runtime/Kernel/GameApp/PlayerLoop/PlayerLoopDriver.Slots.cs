using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 阶段注册表：PlayerLoop 阶段插槽（SubSystem 挂接）与生命周期（Init/Shutdown）。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="PlayerLoopDriver"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal static partial class PlayerLoopDriver
    {

        public static void Initialize()
        {
            if (s_MainThreadId == 0) s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            PlayerLoopInjector.EnsureInjected();
            s_IsShutdown = false;
        }

        /// <summary>
        /// 关闭驱动器：触发 Destroy 订阅、清空全部 Handler / 回调、摘除本框架的 PlayerLoop 系统（幂等）。
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

            // 只摘本框架的三个标记，第三方注入原样保留：Shutdown 并不总意味着进程结束——GameApp.Shutdown
            // 之后可能还要 LoadScene 重启（调试器 OperationsWindow 的 Shutdown (Restart)），或正在退出流程中
            // 等待异步存档落盘。而 UniTask 等第三方 Pump 不会自行重新注入，这些时刻没了 Pump，await 就永不续跑。
            PlayerLoopInjector.RestoreDefault();
        }

        /// <summary>
        /// 清空全部 Handler / 回调订阅，但不广播 Destroy、不动 PlayerLoop。
        /// </summary>
        /// <remarks>由 <see cref="Shutdown"/> 调用；internal 供测试只复位注册表而不触发 <c>SetPlayerLoop</c>。</remarks>
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

            s_IsDriving = false;
        }

        /// <summary>
        /// 测试专用：复位注册表并设置活跃位，不触碰 PlayerLoop 注入。
        /// </summary>
        /// <remarks>EditMode 测试不能走 <see cref="Initialize"/>（其 <c>SetPlayerLoop</c> 会把 Drive 挂进编辑器循环、污染用例）。</remarks>
        internal static void ResetForTests(bool active)
        {
            ClearHandlers();
            s_IsShutdown = !active;
        }

        /// <summary>
        /// SubsystemRegistration：仅复位驱动开关与熔断阈值。
        /// </summary>
        /// <remarks>不在此清空订阅：同阶段初始化顺序未定义，清空可能抹掉已先注册的订阅；订阅表由 <see cref="Shutdown"/> 的优雅路径清空。</remarks>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload()
        {
            s_IsShutdown = true;
            s_FailureTripThreshold = DEFAULT_FAILURE_TRIP_THRESHOLD; // 用户调过的阈值不跨会话残留
            s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }
    }
}
