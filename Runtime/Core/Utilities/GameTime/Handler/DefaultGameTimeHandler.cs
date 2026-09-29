using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认游戏时间处理器：直读 <see cref="Time"/> 引擎时钟，作为 <see cref="GameTime.Handler"/> 的缺省后端。
    /// </summary>
    /// <remarks>
    /// 全部时间读取经 <see cref="GameTimeHandler.ScaledNow"/>/<see cref="GameTimeHandler.UnscaledNow"/> 双精度入口统一供出。
    /// 测试可整体替换为自定义 <see cref="GameTimeHandler"/>（虚拟时钟）实现确定性推进。
    /// </remarks>
    [Serializable]
    internal sealed class DefaultGameTimeHandler : GameTimeHandler
    {
        /// <inheritdoc/>
        public override double ScaledNow => Time.timeAsDouble;

        /// <inheritdoc/>
        public override double UnscaledNow => Time.unscaledTimeAsDouble;

        /// <inheritdoc/>
        public override double RealtimeNow => Time.realtimeSinceStartupAsDouble;

        /// <inheritdoc/>
        public override float DeltaTime => Time.deltaTime;

        /// <inheritdoc/>
        public override float UnscaledDeltaTime => Time.unscaledDeltaTime;

        /// <inheritdoc/>
        public override float FixedDeltaTime => Time.fixedDeltaTime;

        /// <inheritdoc/>
        public override int FrameCount => Time.frameCount;
    }
}
