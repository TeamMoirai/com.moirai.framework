namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 框架级消息事件类型。<b>本枚举只承载框架自身产生的通知</b>；
    /// SDK 登录/支付/切号等业务回调属于项目层语义，请各自定义
    /// <c>EventBase&lt;T&gt;</c> 负载类型投递，不要往这里加——否则每接一个新 SDK
    /// 都要回头改框架核心并重排号段。
    /// </summary>
    public enum EMessageEventType
    {
        // 框架事件，10000起步（该号段为框架保留，项目层请另起号段或另立事件类型）
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
    /// 框架事件
    /// </summary>
    public interface IGameAppEvent { }
    /// <summary>
    /// 框架通知事件。实例来自对象池：投递方在 <c>using</c> 作用域结束时归还，
    /// 因此订阅方<b>不得跨帧持有本实例</b>（含其字段）——需要延后处理，请在回调内把值取出来自用。
    /// </summary>
    public class GameAppMessageEvent : EventBase<GameAppMessageEvent>, IGameAppEvent
    {
        public EMessageEventType EventType { get; private set; }
        
        private static GameAppMessageEvent GetPooled(EMessageEventType eventType)
        {
            var evt = GetPooled();
            evt.EventType = eventType;
            return evt;
        }
        
        private static void Trigger(EMessageEventType eventType)
        {
            // Debug.Log($"GameAppMessageEvent: {eventType}");
            using var evt = GetPooled(eventType);
            EventManager.SendEvent(evt);
        }

        public static void ApplicationFocus() => Trigger(EMessageEventType.ApplicationFocus);
        public static void NotApplicationFocus() => Trigger(EMessageEventType.NotApplicationFocus);
        public static void ApplicationQuit() => Trigger(EMessageEventType.ApplicationQuit);
    }
}