using System;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;

namespace Utility
{
    /// <summary>
    /// <see cref="TweenEase"/> 与 <see cref="EaseUtility"/> 的 EditMode 单元测试。
    /// 缓动函数值与 Robert Penner 参考公式（见 TweenTest.Easing.cs partial）逐点比对；
    /// 零分配契约与隐式转换语义单独覆盖。
    /// </summary>
    public partial class TweenEaseTest
    {
        private static readonly float[] k_Samples = { 0f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 1f };

        #region 缓动函数与参考公式比对 [EASE VS REFERENCE]

        [Test]
        public void Ease_Linear_MatchesReference()
        {
            foreach (float t in k_Samples)
                Assert.AreEqual(Linear(t), EaseUtility.Evaluate(TweenUtility.EEase.Linear, t), 1e-5f, $"t={t}");
        }

        [Test]
        public void Ease_Quadratic_MatchesReference()
        {
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(In_Quadratic(t), EaseUtility.Evaluate(TweenUtility.EEase.InQuad, t), 1e-4f, $"In t={t}");
                Assert.AreEqual(Out_Quadratic(t), EaseUtility.Evaluate(TweenUtility.EEase.OutQuad, t), 1e-4f, $"Out t={t}");
                Assert.AreEqual(InOut_Quadratic(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutQuad, t), 1e-4f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Cubic_MatchesReference()
        {
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(In_Cubic(t), EaseUtility.Evaluate(TweenUtility.EEase.InCubic, t), 1e-4f, $"In t={t}");
                Assert.AreEqual(Out_Cubic(t), EaseUtility.Evaluate(TweenUtility.EEase.OutCubic, t), 1e-4f, $"Out t={t}");
                Assert.AreEqual(InOut_Cubic(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutCubic, t), 1e-4f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Quartic_MatchesReference()
        {
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(In_Quartic(t), EaseUtility.Evaluate(TweenUtility.EEase.InQuart, t), 1e-4f, $"In t={t}");
                Assert.AreEqual(Out_Quartic(t), EaseUtility.Evaluate(TweenUtility.EEase.OutQuart, t), 1e-4f, $"Out t={t}");
                Assert.AreEqual(InOut_Quartic(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutQuart, t), 1e-4f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Quintic_MatchesReference()
        {
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(In_Quintic(t), EaseUtility.Evaluate(TweenUtility.EEase.InQuint, t), 1e-4f, $"In t={t}");
                Assert.AreEqual(Out_Quintic(t), EaseUtility.Evaluate(TweenUtility.EEase.OutQuint, t), 1e-4f, $"Out t={t}");
                Assert.AreEqual(InOut_Quintic(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutQuint, t), 1e-4f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Sinusoidal_MatchesReference()
        {
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(In_Sinusoidal(t), EaseUtility.Evaluate(TweenUtility.EEase.InSine, t), 1e-4f, $"In t={t}");
                Assert.AreEqual(Out_Sinusoidal(t), EaseUtility.Evaluate(TweenUtility.EEase.OutSine, t), 1e-4f, $"Out t={t}");
                Assert.AreEqual(InOut_Sinusoidal(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutSine, t), 1e-4f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Bounce_MatchesStandardPenner()
        {
            // 参考文件 TweenTest.Easing.cs 的 Bounce 是非标准变体（In_Bounce(0.1)=1.25 等），
            // 此处对照双精度标准 Penner 公式（gizma.com/easing）验证
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(1 - StdOutBounce(1 - t), EaseUtility.Evaluate(TweenUtility.EEase.InBounce, t), 1e-3f, $"In t={t}");
                Assert.AreEqual(StdOutBounce(t), EaseUtility.Evaluate(TweenUtility.EEase.OutBounce, t), 1e-3f, $"Out t={t}");
                Assert.AreEqual(StdInOutBounce(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutBounce, t), 1e-3f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Back_MatchesStandardPenner()
        {
            // 参考文件的 Back 常数组合为变体（InOut 中段差 ~6e-2），对照标准 Penner
            const double c1 = 1.70158;
            const double c3 = c1 + 1;
            const double c2 = c1 * 1.525;
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(c3 * t * t * t - c1 * t * t, EaseUtility.Evaluate(TweenUtility.EEase.InBack, t), 1e-3f, $"In t={t}");
                Assert.AreEqual(1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2), EaseUtility.Evaluate(TweenUtility.EEase.OutBack, t), 1e-3f, $"Out t={t}");
                double expected = t < 0.5
                    ? Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2) / 2
                    : (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
                Assert.AreEqual(expected, EaseUtility.Evaluate(TweenUtility.EEase.InOutBack, t), 1e-3f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Exponential_MatchesReference()
        {
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(In_Exponential(t), EaseUtility.Evaluate(TweenUtility.EEase.InExpo, t), 1e-4f, $"In t={t}");
                Assert.AreEqual(Out_Exponential(t), EaseUtility.Evaluate(TweenUtility.EEase.OutExpo, t), 1e-4f, $"Out t={t}");
                Assert.AreEqual(InOut_Exponential(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutExpo, t), 1e-4f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_Elastic_MatchesStandardPenner()
        {
            // 参考文件的 Elastic 为变体（Out_Elastic(0.1)≈1.0），对照标准 Penner
            const double c4 = 2 * Math.PI / 3;
            const double c5 = 2 * Math.PI / 4.5;
            foreach (float t in k_Samples)
            {
                double inE = t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * c4);
                double outE = t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
                double inOut = t == 0 ? 0 : t == 1 ? 1 : t < 0.5
                    ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2
                    : Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5) / 2 + 1;
                Assert.AreEqual(inE, EaseUtility.Evaluate(TweenUtility.EEase.InElastic, t), 1e-3f, $"In t={t}");
                Assert.AreEqual(outE, EaseUtility.Evaluate(TweenUtility.EEase.OutElastic, t), 1e-3f, $"Out t={t}");
                Assert.AreEqual(inOut, EaseUtility.Evaluate(TweenUtility.EEase.InOutElastic, t), 1e-3f, $"InOut t={t}");
            }
        }

        #region 标准 Penner 参照 [STANDARD PENNER REFERENCE]

        private static double StdOutBounce(double x)
        {
            const double n1 = 7.5625;
            const double d1 = 2.75;
            if (x < 1 / d1) return n1 * x * x;
            if (x < 2 / d1) return n1 * (x -= 1.5 / d1) * x + 0.75;
            if (x < 2.5 / d1) return n1 * (x -= 2.25 / d1) * x + 0.9375;
            return n1 * (x -= 2.625 / d1) * x + 0.984375;
        }

        private static double StdInOutBounce(double x)
        {
            return x < 0.5
                ? (1 - StdOutBounce(1 - 2 * x)) / 2
                : (1 + StdOutBounce(2 * x - 1)) / 2;
        }

        #endregion

        [Test]
        public void Ease_Circular_MatchesReference()
        {
            foreach (float t in k_Samples)
            {
                Assert.AreEqual(In_Circular(t), EaseUtility.Evaluate(TweenUtility.EEase.InCirc, t), 1e-4f, $"In t={t}");
                Assert.AreEqual(Out_Circular(t), EaseUtility.Evaluate(TweenUtility.EEase.OutCirc, t), 1e-4f, $"Out t={t}");
                Assert.AreEqual(InOut_Circular(t), EaseUtility.Evaluate(TweenUtility.EEase.InOutCirc, t), 1e-4f, $"InOut t={t}");
            }
        }

        [Test]
        public void Ease_BoundaryValues_ExactEndpoints()
        {
            // 全部缓动端点契约：f(0)=0, f(1)=1（Bounce/Elastic 超调仅存在于内部）
            for (TweenUtility.EEase ease = TweenUtility.EEase.Linear; ease <= TweenUtility.EEase.InOutCirc; ease++)
            {
                Assert.AreEqual(0f, EaseUtility.Evaluate(ease, 0f), 1e-5f, $"{ease} f(0)");
                Assert.AreEqual(1f, EaseUtility.Evaluate(ease, 1f), 1e-5f, $"{ease} f(1)");
            }
        }

        [Test]
        public void Ease_OutOfRange_Clamped()
        {
            // Evaluate 内部 clamp 到 [0,1]（TweenEase.Evaluate 的枚举路径同语义）
            Assert.AreEqual(EaseUtility.Evaluate(TweenUtility.EEase.Linear, 0f), EaseUtility.Evaluate(TweenUtility.EEase.Linear, -0.5f), 1e-6f);
            Assert.AreEqual(EaseUtility.Evaluate(TweenUtility.EEase.Linear, 1f), EaseUtility.Evaluate(TweenUtility.EEase.Linear, 1.5f), 1e-6f);
        }

        #endregion

        #region 零分配契约 [ZERO-ALLOC CONTRACT]

        [Test]
        public void EnumConstruct_IsZeroAlloc_NoCurveOwned()
        {
            var ease = new TweenEase(TweenUtility.EEase.OutQuad);

            Assert.IsTrue(ease.IsEase);
            Assert.IsFalse(ease.IsCurve);
            Assert.IsNull(ease.AnimationCurve, "枚举模式不得持有 AnimationCurve（零分配契约）");
        }

        [Test]
        public void EnumConstruct_Evaluate_MatchesEaseUtility()
        {
            var ease = new TweenEase(TweenUtility.EEase.OutQuad);

            foreach (float t in k_Samples)
                Assert.AreEqual(EaseUtility.Evaluate(TweenUtility.EEase.OutQuad, t), ease.Evaluate(t), 0.00001f, $"t={t}");
        }

        [Test]
        public void DefaultEase_IsLinear()
        {
            var ease = default(TweenEase);

            Assert.IsTrue(ease.IsEase);
            Assert.AreEqual(0.3f, ease.Evaluate(0.3f), 0.00001f);
            Assert.AreEqual(TweenUtility.EEase.Linear, ease.EaseType);
        }

        [Test]
        public void NullCurve_FallsBackToLinear()
        {
            // 隐式转换：显式 null → Linear 枚举模式（零分配契约）
            TweenEase ease = (AnimationCurve)null;

            Assert.IsTrue(ease.IsEase);
            Assert.AreEqual(0.25f, ease.Evaluate(0.25f), 0.00001f);
        }

        [Test]
        public void CurveConstruct_EvaluatesCurve()
        {
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            var ease = new TweenEase(curve);

            Assert.IsTrue(ease.IsCurve);
            Assert.IsNotNull(ease.AnimationCurve);
            Assert.AreEqual(curve.Evaluate(0.25f), ease.Evaluate(0.25f), 0.00001f);
        }

        #endregion
    }
}
