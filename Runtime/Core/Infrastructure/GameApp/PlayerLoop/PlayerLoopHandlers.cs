namespace Moirai.Atropos
{
    /// <summary>
    /// PlayerLoop Update 阶段逻辑处理器。实现类在 <see cref="PlayerLoopDriver"/> 注册后每帧驱动。
    /// <para>性能契约：<see cref="Update"/> 不得产生堆分配（GC Alloc）。</para>
    /// </summary>
    public interface IUpdateHandler
    {
        void Update(float deltaTime, float unscaledDeltaTime);
    }

    /// <summary>
    /// PlayerLoop FixedUpdate 阶段逻辑处理器。
    /// <para>性能契约：<see cref="FixedUpdate"/> 不得产生堆分配（GC Alloc）。</para>
    /// </summary>
    public interface IFixedUpdateHandler
    {
        void FixedUpdate(float fixedDeltaTime, float unscaledDeltaTime);
    }

    /// <summary>
    /// PlayerLoop LateUpdate 阶段逻辑处理器（注入于 PreLateUpdate 末尾，晚于 MonoBehaviour.LateUpdate）。
    /// <para>性能契约：<see cref="LateUpdate"/> 不得产生堆分配（GC Alloc）。</para>
    /// </summary>
    public interface ILateUpdateHandler
    {
        void LateUpdate(float deltaTime, float unscaledDeltaTime);
    }

    /// <summary>
    /// 可选：驱动顺序优先级。数值越小越先执行；未实现者一律按优先级 0 参与排序，
    /// 与同优先级者之间维持注册序（稳定）。
    /// </summary>
    public interface IPlayerLoopPriority
    {
        /// <summary>驱动优先级（升序）。</summary>
        int Priority { get; }
    }
}
