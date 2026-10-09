using System;
using Moirai.Atropos.Events;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// 常规窗口事件
    /// </summary>
    public class UIWindowEvent : EventBase<UIWindowEvent>, IUIEvent
    {
        /// <summary>窗口ID</summary>
        public string WindowId { get; private set; }

        /// <summary>窗口类型</summary>
        public Type WindowType { get; private set; }

        public enum EMode { Show, Close, Hide, CloseAll }
        public EMode Mode { get; private set; }

        private static UIWindowEvent GetPooled(Type windowType, string windowId, EMode mode)
        {
            var evt = GetPooled();
            evt.WindowType = windowType;
            evt.WindowId = windowId;
            evt.Mode = mode;
            return evt;
        }

        /// <summary>
        /// 打开指定弹窗（事件不带载荷）
        /// </summary>
        /// <param name="windowId"></param>
        /// <typeparam name="T"></typeparam>
        public static void Show<T>(string windowId) where T : UIWindow
        {
            using var evt = GetPooled(typeof(T), windowId, EMode.Show);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 打开指定弹窗（事件不带载荷）
        /// </summary>
        /// <param name="windowType"></param>
        /// <param name="windowId"></param>
        public static void Show(Type windowType, string windowId)
        {
            using var evt = GetPooled(windowType, windowId, EMode.Show);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 关闭指定弹窗
        /// </summary>
        /// <param name="windowId"></param>
        /// <typeparam name="T"></typeparam>
        public static void Close<T>(string windowId) where T : UIWindow
        {
            using var evt = GetPooled(typeof(T), windowId, EMode.Close);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 关闭指定弹窗
        /// </summary>
        /// <param name="windowType"></param>
        /// <param name="windowId"></param>
        public static void Close(Type windowType, string windowId)
        {
            using var evt = GetPooled(windowType, windowId, EMode.Close);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 关闭所有弹窗
        /// </summary>
        public static void CloseAll()
        {
            using var evt = GetPooled();
            evt.Mode = EMode.CloseAll;
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 隐藏指定弹窗
        /// </summary>
        /// <param name="windowId"></param>
        /// <typeparam name="T"></typeparam>
        public static void Hide<T>(string windowId) where T : UIWindow
        {
            using var evt = GetPooled(typeof(T), windowId, EMode.Hide);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 隐藏指定弹窗
        /// </summary>
        /// <param name="windowType"></param>
        /// <param name="windowId"></param>
        public static void Hide(Type windowType, string windowId)
        {
            using var evt = GetPooled(windowType, windowId, EMode.Hide);
            EventManager.SendEvent(evt);
        }
    }
}