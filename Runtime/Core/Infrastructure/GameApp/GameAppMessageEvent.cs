namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 框架通知事件（实例来自对象池）。
    /// </summary>
    /// <remarks>投递方在 <c>using</c> 结束时归还，订阅方不得跨帧持有本实例及其字段；需延后处理请先取值。</remarks>
    public partial class GameAppMessageEvent : EventBase<GameAppMessageEvent>, IGameAppEvent
    {
        /// <summary>
        /// 框架级消息事件类型（只承载框架自身产生的通知）。
        /// </summary>
        /// <remarks>SDK 登录 / 支付 / 切号等业务回调属项目层语义，请自定义 <c>EventBase&lt;T&gt;</c> 负载投递，勿扩展本枚举。</remarks>
        public enum EEventType
        {
            // 框架事件，10000起步（该号段为框架保留，项目层请另起号段或另立事件类型）

            /// <summary>
            /// 占位值（无事件语义，不要使用）。
            /// </summary>
            Empty = 10000,

            /// <summary>
            /// 游戏对焦
            /// </summary>
            ApplicationFocus = 10001,

            /// <summary>
            /// 游戏失焦
            /// </summary>
            NotApplicationFocus = 10002,

            /// <summary>
            /// 游戏退出
            /// </summary>
            ApplicationQuit = 10003,
        }
        
        /// <summary>
        /// 事件类型。
        /// </summary>
        public EEventType EventType { get; private set; }

        private static GameAppMessageEvent GetPooled(EEventType eventType)
        {
            var evt = GetPooled();
            evt.EventType = eventType;
            return evt;
        }

        private static void Trigger(EEventType eventType)
        {
            using var evt = GetPooled(eventType);
            EventManager.SendEvent(evt);
        }

        /// <summary>
        /// 投递「游戏对焦」事件。
        /// </summary>
        public static void ApplicationFocus() => Trigger(EEventType.ApplicationFocus);

        /// <summary>
        /// 投递「游戏失焦」事件。
        /// </summary>
        public static void NotApplicationFocus() => Trigger(EEventType.NotApplicationFocus);

        /// <summary>
        /// 投递「游戏退出」事件。
        /// </summary>
        public static void ApplicationQuit() => Trigger(EEventType.ApplicationQuit);
    }
}
