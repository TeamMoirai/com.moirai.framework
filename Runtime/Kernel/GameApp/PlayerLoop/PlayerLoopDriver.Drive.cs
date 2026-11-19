using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 驱动核心：核心钩子（Update/FixedUpdate/LateUpdate）与三阶段驱动循环。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="PlayerLoopDriver"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal static partial class PlayerLoopDriver
    {

        /// <summary>
        /// 设置 Update 阶段核心钩子（覆盖式，传 <c>null</c> 清除）。
        /// </summary>
        /// <remarks>核心钩子先于本阶段全部用户订户执行且不参与熔断；由 <see cref="GameApp"/> 装配。</remarks>
        internal static void SetCoreUpdateCallback(Action callback)
        {
            EnsureMainThread();
            s_CoreUpdate = callback;
        }

        /// <summary>
        /// 设置 FixedUpdate 阶段核心钩子。语义同 <see cref="SetCoreUpdateCallback"/>。
        /// </summary>
        internal static void SetCoreFixedUpdateCallback(Action callback)
        {
            EnsureMainThread();
            s_CoreFixedUpdate = callback;
        }

        /// <summary>
        /// 设置 LateUpdate 阶段核心钩子。语义同 <see cref="SetCoreUpdateCallback"/>。
        /// </summary>
        internal static void SetCoreLateUpdateCallback(Action callback)
        {
            EnsureMainThread();
            s_CoreLateUpdate = callback;
        }

        /// <summary>
        /// 调用核心钩子：按分级策略处置异常，但永不熔断。
        /// </summary>
        private static void InvokeCore(Action core, string stageName)
        {
            if (core == null) return;

            try
            {
                core();
            }
            catch (Exception exception)
            {
                LogUtility.Fatal("PlayerLoop {0} core hook threw: {1}", stageName, exception);
                if (RETHROW_SUBSCRIBER_EXCEPTIONS) throw;
            }
        }


        /// <summary>
        /// PlayerLoop Update 阶段入口（由 <see cref="PlayerLoopInjector"/> 调用）。
        /// </summary>
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

        /// <summary>
        /// PlayerLoop FixedUpdate 阶段入口。
        /// </summary>
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

        /// <summary>
        /// PlayerLoop LateUpdate（PreLateUpdate 末尾）阶段入口。
        /// </summary>
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

        /// <summary>
        /// 提交各阶段延迟缓冲。每帧每阶段各一次，无 pending 时仅两次 Count 读。
        /// </summary>
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
        /// 逐项调用多播回调，单项异常不截断其余项（低频生命周期事件专用）。
        /// </summary>
        /// <remarks>
        /// 开发构建也不上抛，异常按 Error 级带栈记录，避免漏掉释放 / 存档等后续响应。
        /// <c>GetInvocationList</c> 每次调用有分配，不得用于帧热路径。
        /// </remarks>
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

        /// <summary>
        /// 带布尔负载的 <see cref="InvokeAllQuarantined(Action, string)"/> 重载（Focus / Pause）。
        /// </summary>
        private static void InvokeAllQuarantined(Action<bool> callbacks, bool arg, string stageName)
        {
            if (callbacks == null) return;

            Delegate[] invocations = callbacks.GetInvocationList();
            for (int i = 0; i < invocations.Length; i++)
            {
                try
                {
                    ((Action<bool>)invocations[i])(arg);
                }
                catch (Exception exception)
                {
                    LogUtility.Error("PlayerLoop {0} callback threw: {1}", stageName, exception);
                }
            }
        }
    }
}
