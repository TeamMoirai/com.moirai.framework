namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 指定事件处理环境的接口
    /// </summary>
    public interface IEventCoordinator
    {
        /// <summary>
        /// 获取协调器的默认事件处理程序
        /// </summary>
        /// <value></value>
        CallbackEventHandler GetCallbackEventHandler();
        
        /// <summary>
        /// 获取协调器的调度程序
        /// </summary>
        /// <value></value>
        EventDispatcher EventDispatcher { get; }
    }
}
