using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 注册面：接口注册、Action 注册与 Unity 事件注册表。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="PlayerLoopDriver"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal static partial class PlayerLoopDriver
    {

        /// <summary>
        /// 注册 Shutdown / Destroy 广播回调。
        /// </summary>
        public static void AddDestroyCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DestroyCallbacks += callback;
        }

        /// <summary>
        /// 注销 Destroy 广播回调。
        /// </summary>
        public static void RemoveDestroyCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DestroyCallbacks -= callback;
        }

        /// <summary>
        /// 注册 OnDrawGizmos 回调（仅编辑器；由 <see cref="GameAppHost"/> 转发驱动）。
        /// </summary>
        public static void AddDrawGizmosCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosCallbacks += callback;
        }

        /// <summary>
        /// 注销 OnDrawGizmos 回调。
        /// </summary>
        public static void RemoveDrawGizmosCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosCallbacks -= callback;
        }

        /// <summary>
        /// 注册 OnDrawGizmosSelected 回调（仅编辑器）。
        /// </summary>
        public static void AddDrawGizmosSelectedCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosSelectedCallbacks += callback;
        }

        /// <summary>
        /// 注销 OnDrawGizmosSelected 回调。
        /// </summary>
        public static void RemoveDrawGizmosSelectedCallback(Action callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_DrawGizmosSelectedCallbacks -= callback;
        }

        /// <summary>
        /// 注册 ApplicationPause 回调。
        /// </summary>
        public static void AddApplicationPauseCallback(Action<bool> callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationPauseCallbacks += callback;
        }

        /// <summary>
        /// 注销 ApplicationPause 回调。
        /// </summary>
        public static void RemoveApplicationPauseCallback(Action<bool> callback)
        {
            if (callback == null) return;
            EnsureMainThread();
            s_ApplicationPauseCallbacks -= callback;
        }

        /// <summary>
        /// 广播 OnDrawGizmos（由宿主转发，编辑器专用）。
        /// </summary>
        public static void RaiseDrawGizmos()
        {
            s_DrawGizmosCallbacks?.Invoke();
        }

        /// <summary>
        /// 广播 OnDrawGizmosSelected（由宿主转发，编辑器专用）。
        /// </summary>
        public static void RaiseDrawGizmosSelected()
        {
            s_DrawGizmosSelectedCallbacks?.Invoke();
        }

        /// <summary>
        /// 广播 ApplicationPause（由宿主 OnApplicationPause 转发）。
        /// </summary>
        public static void RaiseApplicationPause(bool pauseStatus)
        {
            InvokeAllQuarantined(s_ApplicationPauseCallbacks, pauseStatus, "ApplicationPause");
        }


        /// <summary>
        /// 注册 Update Handler。驱动中调用将延迟到本阶段 Drive 结束后提交。
        /// </summary>
        public static void Register(IUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Update.AddPending(handler);
            else s_Update.Add(handler);
        }

        /// <summary>
        /// 注册 FixedUpdate Handler。
        /// </summary>
        public static void Register(IFixedUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Fixed.AddPending(handler);
            else s_Fixed.Add(handler);
        }

        /// <summary>
        /// 注册 LateUpdate Handler。
        /// </summary>
        public static void Register(ILateUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Late.AddPending(handler);
            else s_Late.Add(handler);
        }

        /// <summary>
        /// 注销 Update Handler。
        /// </summary>
        public static void Unregister(IUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Update.RemovePending(handler);
            else s_Update.Remove(handler);
        }

        /// <summary>
        /// 注销 FixedUpdate Handler。
        /// </summary>
        public static void Unregister(IFixedUpdateHandler handler)
        {
            if (handler == null) return;
            if (s_IsDriving) s_Fixed.RemovePending(handler);
            else s_Fixed.Remove(handler);
        }

        /// <summary>
        /// 注销 LateUpdate Handler。
        /// </summary>
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


        /// <summary>
        /// 注册每帧 Update 回调（同步，不依赖 GameObject / UniTask 延迟）。
        /// </summary>
        public static void AddUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_UpdateCallback.AddPending(callback);
            else s_UpdateCallback.Add(callback);
        }

        /// <summary>
        /// 注册 FixedUpdate 回调。
        /// </summary>
        public static void AddFixedUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_FixedCallback.AddPending(callback);
            else s_FixedCallback.Add(callback);
        }

        /// <summary>
        /// 注册 LateUpdate 回调。
        /// </summary>
        public static void AddLateUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_LateCallback.AddPending(callback);
            else s_LateCallback.Add(callback);
        }

        /// <summary>
        /// 注销 Update 回调。
        /// </summary>
        public static void RemoveUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_UpdateCallback.RemovePending(callback);
            else s_UpdateCallback.Remove(callback);
        }

        /// <summary>
        /// 注销 FixedUpdate 回调。
        /// </summary>
        public static void RemoveFixedUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_FixedCallback.RemovePending(callback);
            else s_FixedCallback.Remove(callback);
        }

        /// <summary>
        /// 注销 LateUpdate 回调。
        /// </summary>
        public static void RemoveLateUpdateCallback(Action callback)
        {
            if (callback == null) return;
            if (s_IsDriving) s_LateCallback.RemovePending(callback);
            else s_LateCallback.Remove(callback);
        }
    }
}
