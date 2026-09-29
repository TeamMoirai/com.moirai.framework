using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏时间处理器基类（策略模式抽象策略），框架内置 <see cref="DefaultGameTimeHandler"/> 引擎时钟实现。
    /// </summary>
    /// <remarks>
    /// 双精度读取点 <see cref="ScaledNow"/>/<see cref="UnscaledNow"/>/<see cref="RealtimeNow"/> 是实时直读而非帧缓存，供计时器等对精度敏感的服务在帧内任意时刻读取。 <br />
    /// 帧采样快照由 <see cref="GameTime.StartFrame"/> 每帧统一拉取。 <br />
    /// 测试注入自定义实现（覆写 <see cref="ScaledNow"/>/<see cref="UnscaledNow"/>）即可让依赖时间的服务获得与 Unity 主循环无关的确定性推进。
    /// </remarks>
    [Serializable]
    public abstract class GameTimeHandler : FrameworkHandler
    {
        /// <summary>
        /// 当前缩放时间（秒，双精度，实时直读），引擎时钟下同 <c>Time.timeAsDouble</c>。
        /// </summary>
        /// <remarks>帧内单调递增。</remarks>
        public abstract double ScaledNow { get; }

        /// <summary>
        /// 当前非缩放时间（秒，双精度，实时直读），引擎时钟下同 <c>Time.unscaledTimeAsDouble</c>。
        /// </summary>
        public abstract double UnscaledNow { get; }

        /// <summary>
        /// 当前真实墙钟（秒，双精度，自进程启动起算），不受 timeScale / 暂停 / 播放态影响。
        /// </summary>
        /// <remarks>
        /// 供 TTL 驱逐、失败冷却这类「真实流逝」语义的组件读取；引擎时钟下同 <c>Time.realtimeSinceStartupAsDouble</c>。 <br />
        /// 与 <see cref="UnscaledNow"/> 的差别：编辑器未播放或应用暂停期间 unscaledTime 停止推进，realtime 始终推进。 <br />
        /// 默认取 <see cref="UnscaledNow"/>，无真实墙钟概念的虚拟实现因此自动获得可注入的确定性时间源。
        /// </remarks>
        public virtual double RealtimeNow => UnscaledNow;

        /// <summary>
        /// 此帧开始时的缩放时间（秒）。由 <see cref="GameTime.StartFrame"/> 每帧采样，默认取自 <see cref="ScaledNow"/>。
        /// </summary>
        public virtual float ScaledTime => (float)ScaledNow;

        /// <summary>
        /// 此帧开始时的非缩放时间（秒）。由 <see cref="GameTime.StartFrame"/> 每帧采样，默认取自 <see cref="UnscaledNow"/>。
        /// </summary>
        public virtual float UnscaledTime => (float)UnscaledNow;

        /// <summary>
        /// 从上一帧到当前帧的间隔（秒，受缩放影响）。
        /// </summary>
        /// <remarks>虚拟时钟等无帧概念的实现保持默认 0。</remarks>
        public virtual float DeltaTime => 0f;

        /// <summary>
        /// 从上一帧到当前帧的独立时间间隔（秒，不受缩放影响）。
        /// </summary>
        /// <remarks>虚拟时钟等无帧概念的实现保持默认 0。</remarks>
        public virtual float UnscaledDeltaTime => 0f;

        /// <summary>
        /// 执行物理和其他固定帧速率更新的时间间隔（秒）。
        /// </summary>
        /// <remarks>虚拟时钟等无帧概念的实现保持默认 0。</remarks>
        public virtual float FixedDeltaTime => 0f;

        /// <summary>
        /// 自游戏开始以来的总帧数。
        /// </summary>
        /// <remarks>虚拟时钟等无帧概念的实现保持默认 0。</remarks>
        public virtual int FrameCount => 0;
    }
}
