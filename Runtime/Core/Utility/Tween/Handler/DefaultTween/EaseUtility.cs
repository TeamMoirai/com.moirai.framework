using Unity.Burst;
using static Unity.Mathematics.math;

namespace Moirai.Atropos
{
	/// <summary>
	/// 提供缓动函数计算的工具类。
	/// </summary>
	[BurstCompile]
    public static partial class EaseUtility
    {
        // Core methods ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// 根据 currentTime 在 startValue 和 endValue 之间沿指定的补间曲线移动值
        /// </summary>
        /// <param name="currentTime"></param>
        /// <param name="initialTime"></param>
        /// <param name="endTime"></param>
        /// <param name="startValue"></param>
        /// <param name="endValue"></param>
        /// <param name="curve"></param>
        /// <returns></returns>
        [BurstCompile]
        public static float Tween(float currentTime, float initialTime, float endTime, float startValue, float endValue, EEaseType curve)
        {
            currentTime = MathsUtility.Remap(currentTime, initialTime, endTime, 0f, 1f);
            currentTime = Evaluate(currentTime, curve);
            return startValue + currentTime * (endValue - startValue);
        }
        
        [BurstCompile]
        public static long Tween(float currentTime, float initialTime, float endTime, long startValue, long endValue, EEaseType curve)
        {
	        currentTime = MathsUtility.Remap(currentTime, initialTime, endTime, 0f, 1f);
	        currentTime = Evaluate(currentTime, curve);
	        return startValue + (long)(currentTime * (endValue - startValue));
        }
        
        [BurstCompile]
        public static float Evaluate(float t, EEaseType ease)
        {
	        return ease switch
	        {
		        EEaseType.Linear => Linear(t),
		        
		        EEaseType.InQuad => InQuadratic(t),
		        EEaseType.OutQuad => OutQuadratic(t),
		        EEaseType.InOutQuad => InOutQuadratic(t),
		        
		        EEaseType.InCubic => InCubic(t),
		        EEaseType.OutCubic => OutCubic(t),
		        EEaseType.InOutCubic => InOutCubic(t),
		        
		        EEaseType.InQuart => InQuartic(t),
		        EEaseType.OutQuart => OutQuartic(t),
		        EEaseType.InOutQuart => InOutQuartic(t),
		        
		        EEaseType.InQuint => InQuintic(t),
		        EEaseType.OutQuint => OutQuintic(t),
		        EEaseType.InOutQuint => InOutQuintic(t),
		        
		        EEaseType.InSine => InSinusoidal(t),
		        EEaseType.OutSine => OutSinusoidal(t),
		        EEaseType.InOutSine => InOutSinusoidal(t),
		        
		        EEaseType.InBounce => InBounce(t),
		        EEaseType.OutBounce => OutBounce(t),
		        EEaseType.InOutBounce => InOutBounce(t),
		        
		        EEaseType.InBack => InBack(t),
		        EEaseType.OutBack => OutBack(t),
		        EEaseType.InOutBack => InOutBack(t),
		        
		        EEaseType.InExpo => InExponential(t),
		        EEaseType.OutExpo => OutExponential(t),
		        EEaseType.InOutExpo => InOutExponential(t),
		        
		        EEaseType.InElastic => InElastic(t),
		        EEaseType.OutElastic => OutElastic(t),
		        EEaseType.InOutElastic => InOutElastic(t),
		        
		        EEaseType.InCirc => InCircular(t),
		        EEaseType.OutCirc => OutCircular(t),
		        EEaseType.InOutCirc => InOutCircular(t),
		        
		        EEaseType.AntiLinear => AntiLinear(t),
		        EEaseType.AlmostIdentity => AlmostIdentity(t),
		        
		        _ => t,
	        };
        }
        
        // Linear 线性匀速运动效果 ---------------------------------------------------------------------------------------------------------------------------

        [BurstCompile]
        public static float Linear(float t) => t;
        
        [BurstCompile]
		public static float AntiLinear(float t) => 1 - t;
		
		// Almost Identity 

		[BurstCompile]
		public static float AlmostIdentity(float t) => t * t * (2.0f - t);
		
		// Quadratic 二次方的缓动（t^2） ---------------------------------------------------------------------------------------------------------------------------

		[BurstCompile]
		public static float InQuadratic(float t) => t * t;

		[BurstCompile]
		public static float OutQuadratic(float t) => 1 - (1 - t) * (1 - t);

		[BurstCompile]
		public static float InOutQuadratic(float t) => t < 0.5f ? 2 * t * t : 1 - pow(-2 * t + 2, 2) / 2;
		
		// Cubic 三次方的缓动（t^3） ---------------------------------------------------------------------------------------------------------------------------

		[BurstCompile]
		public static float InCubic(float t) => t * t * t;

		[BurstCompile]
		public static float OutCubic(float t) => 1 - pow(1 - t, 3);

		[BurstCompile]
		public static float InOutCubic(float t) => t < 0.5f ? 4 * t * t * t : 1 - pow(-2 * t + 2, 3) / 2;

		// Quartic 四次方的缓动（t^4） ---------------------------------------------------------------------------------------------------------------------------
		
		[BurstCompile]
		public static float InQuartic(float t) => t * t * t * t;

		[BurstCompile]
		public static float OutQuartic(float t) => 1 - pow(1 - t, 4);

		[BurstCompile]
		public static float InOutQuartic(float t) => t < 0.5 ? 8 * t * t * t * t : 1 - pow(-2 * t + 2, 4) / 2;
		
		// Quintic 五次方的缓动（t^5） ---------------------------------------------------------------------------------------------------------------------------

		[BurstCompile]
		public static float InQuintic(float t) => t * t * t * t * t;

		[BurstCompile]
		public static float OutQuintic(float t) => 1 - pow(1 - t, 5);

		[BurstCompile]
		public static float InOutQuintic(float t) => t < 0.5f ? 16 * t * t * t * t * t : 1 - pow(-2 * t + 2, 5) / 2;
		
		// Sinusoidal 正弦曲线的缓动（sin(t)） ---------------------------------------------------------------------------------------------------------------------------

		/// <remarks>原始实现使用 sin(PI/2 * t - PI/2)，可通过三角恒等式简化为 -cos(PI/2 * t)</remarks>
		[BurstCompile]
		public static float InSinusoidal(float t) => 1 - cos(PI / 2 * t);
		
		/// <remarks>
		/// 原始实现使用 1 - InSinusoidal(1-t)，根据数学恒等式可展开为 cos(PI/2 * (1 - t))，
		/// 利用三角恒等式 cos(PI/2*(1-t)) = sin(PI/2*t) 可将表达式进一步优化为 sin(PI/2 * t)，减少计算步骤。
		/// </remarks>
		[BurstCompile]
		public static float OutSinusoidal(float t) => sin(PI / 2f * t);
		
		/// <remarks>当t在0到1时，cos(PI * t)从1到-1，所以1 - cos(PI * t)从0到2，乘以0.5得到0到1，这与原函数的结果相同</remarks>
		[BurstCompile]
		public static float InOutSinusoidal(float t) => 0.5f * (1 - cos(PI * t));
		
		// Bounce 指数衰减的反弹缓动 ---------------------------------------------------------------------------------------------------------------------------

		[BurstCompile]
		public static float InBounce(float t) => 1 - OutBounce(1 - t);
		
		[BurstCompile]
		public static float OutBounce(float t)
		{
			const float firstBounceAmpl = 0.75f;
			const float n1 = 7.5625f;
			const float d1 = 2.75f;

			if (t < 1 / d1)
			{
				return n1 * t * t;
			}
			else if (t < 2 / d1)
			{
				return n1 * (t -= 1.5f / d1) * t + firstBounceAmpl;
			}
			else if (t < 2.5 / d1)
			{
				return n1 * (t -= 2.25f / d1) * t + 0.9375f;
			}
			else
			{
				return n1 * (t -= 2.625f / d1) * t + 0.984375f;
			}
		}
		
		[BurstCompile]
		public static float InOutBounce(float t) =>
			t < 0.5f ?
				(1 - OutBounce(1 - 2 * t)) / 2 :
				(1 + OutBounce(2 * t - 1)) / 2;

		// Back 超过范围的三次方缓动（(s+1)*t^3 – s*t^2） ---------------------------------------------------------------------------------------------------------------------------

		[BurstCompile]
		public static float InBack(float t)
		{
			const float c1 = 1.70158f;
			const float c3 = c1 + 1;
			return c3 * t * t * t - c1 * t * t;
		}

		[BurstCompile]
		public static float OutBack(float t)
		{
			const float c1 = 1.70158f;
			const float c3 = c1 + 1;
			return 1 + c3 * pow(t - 1, 3) + c1 * pow(t - 1, 2);
		}

		[BurstCompile]
		public static float InOutBack(float t)
		{
			const float c1 = 1.70158f;
			const float c2 = c1 * 1.525f;

			return t < 0.5f
				? pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2) / 2
				: (pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
		}

		// Exponential 指数曲线的缓动（2^t） ---------------------------------------------------------------------------------------------------------------------------

		[BurstCompile]
		public static float InExponential(float t) => t == 0 ? 0 : pow(2, 10 * t - 10);

		[BurstCompile]
		public static float OutExponential(float t) => t == 1 ? 1 : 1 - pow(2, -10 * t);

		[BurstCompile]
		public static float InOutExponential(float t)
		{
			return t switch
			{
				0 => 0,
				1 => 1,
				_ => t < 0.5f ? pow(2, 20 * t - 10) / 2 : (2 - pow(2, -20 * t + 10)) / 2
			};
		}
		
		// Elastic 指数衰减的正弦曲线缓动     ---------------------------------------------------------------------------------------------------------------------------
		
		[BurstCompile]
		public static float InElastic(float t)
		{
			const float c4 = 2 * PI / 3;
			return t switch
			{
				0 => 0,
				1 => 1,
				_ => -pow(2, 10 * t - 10) * sin((t * 10 - 10.75f) * c4)
			};
		}

		[BurstCompile]
		public static float OutElastic(float t)
		{
			const float c4 = 2 * PI / 3;
			return t switch
			{
				0 => 0,
				1 => 1,
				_ => pow(2, -10 * t) * sin((t * 10 - 0.75f) * c4) + 1
			};
		}

		[BurstCompile]
		public static float InOutElastic(float t)
		{
			const float c5 = 2 * PI / 4.5f;

			return t switch
			{
				0 => 0,
				1 => 1,
				_ => t < 0.5f
					? -(pow(2, 20 * t - 10) * sin((20 * t - 11.125f) * c5)) / 2
					: pow(2, -20 * t + 10) * sin((20 * t - 11.125f) * c5) / 2 + 1
			};
		}
		
		// Circular 圆形曲线的缓动（sqrt(1-t^2)）    ---------------------------------------------------------------------------------------------------------------------------

		[BurstCompile]
		public static float InCircular(float t) => 1 - sqrt(1 - pow(t, 2));

		[BurstCompile]
		public static float OutCircular(float t) => sqrt(1 - pow(t - 1, 2));

		[BurstCompile]
		public static float InOutCircular(float t) =>
			t < 0.5 ?
				(1 - sqrt(1 - pow(2 * t, 2))) / 2 :
				(sqrt(1 - pow(-2 * t + 2, 2)) + 1) / 2;
    }
}