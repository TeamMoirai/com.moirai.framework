namespace Moirai.Atropos
{
    public static partial class TweenUtility
    {
        /// <summary>
        /// 循环模式。
        /// </summary>
        public enum ECycleMode
        {
            /// <summary>
            /// 重新开始补间动画。
            /// </summary>
            Restart,

            /// <summary>
            /// 像悠悠球一样来回摆动，回程的缓动效果与去程相同。
            /// </summary>
            Yoyo,

            /// <summary>
            /// 在循环结束时，将`endValue`增加`startValue`与`endValue`之间的差值。
            /// </summary>
            /// <example>例如，如果一个补间动画将position.x从0移动到1，那么在第一个周期结束后，补间动画会继续将position.x从1移动到2，以此类推。</example>
            Incremental,

            /// <summary>
            /// 将补间动画倒回，如同时间倒流。在反向循环时，缓动效果也会反转。
            /// </summary>
            Rewind,
        }
    }
}