using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏时间处理器基类（策略模式抽象策略）。
    /// <para>框架内置 <see cref="DefaultGameTimeHandler"/>（UnityEngine.Time 引擎时钟）。
    /// 通过替换 <see cref="GameTime.Handler"/> 即可在不同时间源之间零成本切换，调用方代码无需任何改动。</para>
    /// <para>虚拟时钟接缝：测试注入自定义实现（覆写 <see cref="ScaledNow"/>/<see cref="UnscaledNow"/>），
    /// 即可让 Timer 等时间服务获得与 Unity 主循环无关的确定性推进。</para>
    /// <para>双精度读取点为实时直读（非帧缓存），供计时器等对精度敏感的服务在帧内任意时刻读取；
    /// 帧采样快照则由 <see cref="GameTime.StartFrame"/> 每帧统一拉取。</para>
    /// </summary>
    [Serializable]
    public abstract class GameTimeHandler : FrameworkHandler
    {
        /// <summary>
        /// 当前缩放时间（秒，双精度，实时直读）。
        /// <para>引擎时钟下同 <c>Time.timeAsDouble</c>；帧内单调递增。</para>
        /// </summary>
        public abstract double ScaledNow { get; }

        /// <summary>
        /// 当前非缩放时间（秒，双精度，实时直读）。
        /// <para>引擎时钟下同 <c>Time.unscaledTimeAsDouble</c>。</para>
        /// </summary>
        public abstract double UnscaledNow { get; }

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
        /// <para>虚拟时钟等无帧概念的实现保持默认 0。</para>
        /// </summary>
        public virtual float DeltaTime => 0f;

        /// <summary>
        /// 从上一帧到当前帧的独立时间间隔（秒，不受缩放影响）。
        /// <para>虚拟时钟等无帧概念的实现保持默认 0。</para>
        /// </summary>
        public virtual float UnscaledDeltaTime => 0f;

        /// <summary>
        /// 执行物理和其他固定帧速率更新的时间间隔（秒）。
        /// <para>虚拟时钟等无帧概念的实现保持默认 0。</para>
        /// </summary>
        public virtual float FixedDeltaTime => 0f;

        /// <summary>
        /// 自游戏开始以来的总帧数。
        /// <para>虚拟时钟等无帧概念的实现保持默认 0。</para>
        /// </summary>
        public virtual int FrameCount => 0;
    }
}
