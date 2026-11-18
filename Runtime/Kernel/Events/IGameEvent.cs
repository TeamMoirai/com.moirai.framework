namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏事件标记接口：IEventBus 体系的事件必须实现本接口且为 struct（零拷贝发布）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Moirai.Atropos.Events.EventBase{T}"/> 池化 class 事件体系互斥——总线事件是无状态数据包， <br />
    /// 不进池、不回引用、不经树形传播（TrickleDown/BubbleUp 是 UI/Task 专用能力，留在 EventBase）。 <br />
    /// 带大块引用负载的高频事件可把负载装箱为 class 字段（struct 事件携带引用，仍是零拷贝传参）。
    /// </remarks>
    public interface IGameEvent
    {
    }
}
