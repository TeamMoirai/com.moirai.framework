namespace Moirai.Atropos
{
    /// <summary>
    /// PlayerLoop Update 阶段逻辑处理器。
    /// </summary>
    /// <remarks>在 <see cref="PlayerLoopDriver"/> 注册后每帧驱动；<see cref="Update"/> 不得产生堆分配。</remarks>
    public interface IUpdateHandler
    {
        void Update(float deltaTime, float unscaledDeltaTime);
    }

    /// <summary>
    /// PlayerLoop FixedUpdate 阶段逻辑处理器。
    /// </summary>
    /// <remarks><see cref="FixedUpdate"/> 不得产生堆分配。</remarks>
    public interface IFixedUpdateHandler
    {
        void FixedUpdate(float fixedDeltaTime, float unscaledDeltaTime);
    }

    /// <summary>
    /// PlayerLoop LateUpdate 阶段逻辑处理器（晚于 <c>MonoBehaviour.LateUpdate</c>）。
    /// </summary>
    /// <remarks><see cref="LateUpdate"/> 不得产生堆分配。</remarks>
    public interface ILateUpdateHandler
    {
        void LateUpdate(float deltaTime, float unscaledDeltaTime);
    }

    /// <summary>
    /// 可选：驱动顺序优先级（数值越小越先执行）。
    /// </summary>
    /// <remarks>未实现者按优先级 0 参与排序，同优先级之间维持注册序。</remarks>
    public interface IPlayerLoopPriority
    {
        /// <summary>驱动优先级（升序）。</summary>
        int Priority { get; }
    }
}
