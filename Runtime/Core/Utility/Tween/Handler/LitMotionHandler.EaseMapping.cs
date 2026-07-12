#if LITMOTION_INSTALLED
using LitMotion;

namespace Moirai.Atropos
{
    public static class LitMotionEaseMapping
    {
        public static Ease ToLitMotionEase(this TweenUtility.EEase ease)
        {
            return ease switch
            {
                TweenUtility.EEase.Linear => Ease.Linear,
                TweenUtility.EEase.InSine => Ease.InSine,
                TweenUtility.EEase.OutSine => Ease.OutSine,
                TweenUtility.EEase.InOutSine => Ease.InOutSine,
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
                TweenUtility.EEase.InExpo => Ease.InExpo,
                TweenUtility.EEase.OutExpo => Ease.OutExpo,
                TweenUtility.EEase.InOutExpo => Ease.InOutExpo,
                TweenUtility.EEase.InCirc => Ease.InCirc,
                TweenUtility.EEase.OutCirc => Ease.OutCirc,
                TweenUtility.EEase.InOutCirc => Ease.InOutCirc,
                TweenUtility.EEase.InElastic => Ease.InElastic,
                TweenUtility.EEase.OutElastic => Ease.OutElastic,
                TweenUtility.EEase.InOutElastic => Ease.InOutElastic,
                TweenUtility.EEase.InBack => Ease.InBack,
                TweenUtility.EEase.OutBack => Ease.OutBack,
                TweenUtility.EEase.InOutBack => Ease.InOutBack,
                TweenUtility.EEase.InBounce => Ease.InBounce,
                TweenUtility.EEase.OutBounce => Ease.OutBounce,
                TweenUtility.EEase.InOutBounce => Ease.InOutBounce,
                _ => Ease.Linear
            };
        }

        public static LoopType ToLitMotionLoopType(this TweenUtility.ECycleMode cycleMode)
        {
            return cycleMode switch
            {
                TweenUtility.ECycleMode.Restart => LoopType.Restart,
                TweenUtility.ECycleMode.Yoyo => LoopType.Yoyo,
                TweenUtility.ECycleMode.Incremental => LoopType.Incremental,
                _ => LoopType.Restart
            };
        }
    }
}
#endif
