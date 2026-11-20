namespace Moirai.Atropos
{
    public static partial class TweenUtility
    {
        /// <summary>
        /// 循环模式。
        /// </summary>
        public enum ECycleMode
        {
            /// <summary>从头播放。</summary>
            /// <remarks>重新开始补间动画。</remarks>
            Restart,

            /// <summary>正反交替。</summary>
            /// <remarks>回程交换起止值并重新施加同一条缓动曲线，缓动效果与去程相同。</remarks>
            Yoyo,

            /// <summary>在终点值基础上累加。</summary>
            /// <remarks>在循环结束时，将`endValue`增加`startValue`与`endValue`之间的差值。</remarks>
            /// <example>例：position.x 从 0→1，首个周期结束后继续 1→2，依此类推。</example>
            Incremental,

            /// <summary>反向回放补间；反向循环时缓动效果随之反转。</summary>
            /// <remarks>实现为原轨迹的时间反演：反向周期的取值 = 起始值 + ease(1-t) × 差值，
            /// 与 Yoyo（交换起止值后重新施加缓动）轨迹不同——非对称缓动（如 OutQuad）下两者中点值不同。</remarks>
            Rewind,
        }
    }
}