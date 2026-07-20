namespace Moirai.Atropos
{
    public static partial class TweenUtility
    {
        /// <summary>
        /// 动画曲线类型。
        /// </summary>
        public enum EEase
        {
            Linear = 0,
            InQuad = 1,       OutQuad = 2,       InOutQuad = 3,
            InCubic = 4,      OutCubic = 5,      InOutCubic = 6,
            InQuart = 7,      OutQuart = 8,      InOutQuart = 9,
            InQuint = 10,     OutQuint = 11,     InOutQuint = 12,
            InSine = 13,      OutSine = 14,      InOutSine = 15,
            InBounce = 16,    OutBounce = 17,    InOutBounce = 18,
            InBack = 19,      OutBack = 20,      InOutBack = 21,
            InExpo = 22,      OutExpo = 23,      InOutExpo = 24,
            InElastic = 25,   OutElastic = 26,   InOutElastic = 27,
            InCirc = 28,      OutCirc = 29,      InOutCirc = 30,
            // AntiLinear = 31,  AlmostIdentity = 32
        }
    }
}