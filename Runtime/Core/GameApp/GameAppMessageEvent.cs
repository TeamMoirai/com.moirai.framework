namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 框架通知事件。实例来自对象池：投递方在 <c>using</c> 作用域结束时归还，
    /// 因此订阅方<b>不得跨帧持有本实例</b>（含其字段）——需要延后处理，请在回调内把值取出来自用。
    /// </summary>
    public class GameAppMessageEvent : EventBase<GameAppMessageEvent>, IGameAppEvent
    {
        /// <summary>
        /// 事件类型。
        /// </summary>
        public EMessageEventType EventType { get; private set; }

        private static GameAppMessageEvent GetPooled(EMessageEventType eventType)
        {
            var evt = GetPooled();
            evt.EventType = eventType;
            return evt;
        }

        private static void Trigger(EMessageEventType eventType)
        {
            using var evt = GetPooled(eventType);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 投递「游戏对焦」事件。
        /// </summary>
        public static void ApplicationFocus() => Trigger(EMessageEventType.ApplicationFocus);

        /// <summary>
        /// 投递「游戏失焦」事件。
        /// </summary>
        public static void NotApplicationFocus() => Trigger(EMessageEventType.NotApplicationFocus);

        /// <summary>
        /// 投递「游戏退出」事件。
        /// </summary>
        public static void ApplicationQuit() => Trigger(EMessageEventType.ApplicationQuit);
    }
}
