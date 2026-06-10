using Moirai.Atropos.Events;

namespace Moirai.Atropos.UI
{
    public interface IUIEvent { }
    public class UIModuleEvent : EventBase<UIModuleEvent>, IUIEvent
    {
        public UIWindow Window { get; private set; }
        public enum EMode { Shown, Closed }
        public EMode Mode { get; private set; }

        private static UIModuleEvent GetPooled(UIWindow window, EMode mode)
        {
            var evt = GetPooled();
            evt.Window = window;
            evt.Mode = mode;
            return evt;
        }

        /// <summary>
        /// 窗口打开后触发的事件
        /// </summary>
        public static void Shown(UIWindow window)
        {
            using var evt = GetPooled(window, EMode.Shown);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 关闭窗口后触发的事件
        /// </summary>
        public static void Closed(UIWindow window)
        {
            using var evt = GetPooled(window, EMode.Closed);
            EventManager.SendEvent(evt);
        }
    }
}