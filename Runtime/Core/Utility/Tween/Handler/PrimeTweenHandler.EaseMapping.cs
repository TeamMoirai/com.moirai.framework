#if PRIMETWEEN_INSTALLED
using System;
using PrimeTween;

namespace Moirai.Atropos
{
    public static class PrimeTweenMapping
    {
        /// <summary>
        /// 将 <see cref="TweenUtility.EEase"/> 枚举转换为PrimeTween的Ease枚举。
        /// </summary>
        /// <param name="ease">EEase枚举值。</param>
        /// <returns>对应的PrimeTween的Ease枚举值。</returns>
        public static Ease ToPrimeTweenEase(this TweenUtility.EEase ease)
        {
            return ease switch
            {
                TweenUtility.EEase.Linear => Ease.Linear,

                TweenUtility.EEase.InQuad => Ease.InQuad,
                TweenUtility.EEase.OutQuad => Ease.OutQuad,
                TweenUtility.EEase.InOutQuad => Ease.InOutQuad,

                TweenUtility.EEase.InCubic => Ease.InCubic,
                TweenUtility.EEase.OutCubic => Ease.OutCubic,
                TweenUtility.EEase.InOutCubic => Ease.InOutCubic,

                TweenUtility.EEase.InQuart => Ease.InQuart,
                TweenUtility.EEase.OutQuart => Ease.OutQuart,
                TweenUtility.EEase.InOutQuart => Ease.InOutQuart,

                TweenUtility.EEase.InQuint => Ease.InQuint,
                TweenUtility.EEase.OutQuint => Ease.OutQuint,
                TweenUtility.EEase.InOutQuint => Ease.InOutQuint,

                TweenUtility.EEase.InSine => Ease.InSine,
                TweenUtility.EEase.OutSine => Ease.OutSine,
                TweenUtility.EEase.InOutSine => Ease.InOutSine,

                TweenUtility.EEase.InBounce => Ease.InBounce,
                TweenUtility.EEase.OutBounce => Ease.OutBounce,
                TweenUtility.EEase.InOutBounce => Ease.InOutBounce,

                TweenUtility.EEase.InBack => Ease.InBack,
                TweenUtility.EEase.OutBack => Ease.OutBack,
                TweenUtility.EEase.InOutBack => Ease.InOutBack,

                TweenUtility.EEase.InExpo => Ease.InExpo,
                TweenUtility.EEase.OutExpo => Ease.OutExpo,
                TweenUtility.EEase.InOutExpo => Ease.InOutExpo,

                TweenUtility.EEase.InElastic => Ease.InElastic,
                TweenUtility.EEase.OutElastic => Ease.OutElastic,
                TweenUtility.EEase.InOutElastic => Ease.InOutElastic,

                TweenUtility.EEase.InCirc => Ease.InCirc,
                TweenUtility.EEase.OutCirc => Ease.OutCirc,
                TweenUtility.EEase.InOutCirc => Ease.InOutCirc,

                _ => throw new ArgumentOutOfRangeException(nameof(ease), ease, null)
            };
        }

        /// <summary>
        /// 将 <see cref="TweenUtility.ECycleMode"/> 枚举转换为PrimeTween的CycleMode枚举。
        /// </summary>
        /// <param name="cycleMode">ECycleMode枚举值。</param>
        /// <returns>对应的PrimeTween的CycleType枚举值。</returns>
        public static CycleMode ToPrimeTweenCycleMode(this TweenUtility.ECycleMode cycleMode)
        {
            return cycleMode switch
            {
                TweenUtility.ECycleMode.Restart => CycleMode.Restart,
                TweenUtility.ECycleMode.Yoyo => CycleMode.Yoyo,
                TweenUtility.ECycleMode.Incremental => CycleMode.Incremental,
                TweenUtility.ECycleMode.Rewind => CycleMode.Rewind,

                _ => throw new ArgumentOutOfRangeException(nameof(cycleMode), cycleMode, null)
            };
        }
    }
}
#endif