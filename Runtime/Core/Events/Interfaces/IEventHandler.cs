using UnityEngine;

namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 能够处理事件类的接口。
    /// </summary>
    public interface IEventHandler
    {
        /// <summary>
        /// 将事件发送到事件处理程序。
        /// </summary>
        /// <param name="e">要发送的事件。</param>
        /// <param name="dispatchMode">事件调度模式。</param>
        void SendEvent(EventBase e, DispatchMode dispatchMode = DispatchMode.Default);

        /// <summary>
        /// 处理事件。
        /// </summary>
        /// <param name="evt">要处理的事件。</param>
        void HandleEvent(EventBase evt);

    }
    /// <summary>
    /// 类接口有 <see cref="MonoBehaviour"/> 生命周期范围（lifetime scope）
    /// </summary>
    public interface IBehaviourScope
    {
        /// <summary>
        /// 附加到 <see cref="MonoBehaviour"/>
        /// </summary>
        MonoBehaviour Behaviour { get; }
    }
}
