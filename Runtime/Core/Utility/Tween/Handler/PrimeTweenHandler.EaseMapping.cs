#if PRIMETWEEN_INSTALLED
using PrimeTween;

namespace Moirai.Atropos
{
    public sealed partial class PrimeTweenHandler
    {
        /// <summary>
        /// 将 <see cref="TweenUtility.EEase"/> 枚举转换为PrimeTween的Ease枚举。
        /// </summary>
        /// <param name="ease">EEase枚举值。</param>
        /// <returns>对应的PrimeTween的Ease枚举值。</returns>
        private static Ease GetEase(TweenUtility.EEase ease)
        {
            return (Ease)(int)ease;
        }

        /// <summary>
        /// 将 <see cref="TweenUtility.ECycleMode"/> 枚举转换为PrimeTween的CycleMode枚举。
        /// </summary>
        /// <param name="cycleMode">ECycleMode枚举值。</param>
        /// <returns>对应的PrimeTween的CycleType枚举值。</returns>
        private static CycleMode GetCycleMode(TweenUtility.ECycleMode cycleMode)
        {
            return (CycleMode)(int)cycleMode;
        }
    }
}
#endif