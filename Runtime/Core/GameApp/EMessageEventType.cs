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
}
