namespace Moirai.Atropos.Events
{
    public enum EMessageEventType
    {
        // 框架事件，10000起步
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
        
        // SDK回调信息
        SDKOnInitSuccess = 10004,
        SDKOnInitFail = 10005,
        SDKOnLoginSuccess = 10006,
        SDKOnLoginFail = 10007,
        SDKOnSwitchAccountSuccess = 10008,
        SDKOnLogoutSuccess = 10009,
        SDKOnPaySuccess = 10010,
        SDKOnPayFail = 10011,
        SDKOnPayCancel = 10012,
        SDKOnExitSuccess = 10013,
    }
    
    /// <summary>
    /// 框架事件
    /// </summary>
    public interface IGameAppEvent { }
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