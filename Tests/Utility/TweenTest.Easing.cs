using UnityEngine;

namespace Utility
{
    /// <summary>
    /// 这里的公式基于罗伯特·彭纳（Robert Penner）的缓动方程
    /// http://robertpenner.com/easing/
    /// </summary>
    /// <remarks>未优化版本，仅作实现参考</remarks>
    public partial class TweenEaseTest
    {
        // Linear       ---------------------------------------------------------------------------------------------------------------------------

        public static float Linear(float t)
        {
            return t;
        }

        public static float LinearAnti(float t)
        {
            return 1 - t;
        }

        // Almost Identity 

        public static float AlmostIdentity(float t)
        {
            return t * t * (2.0f - t);
        }

        // Quadratic    ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Quadratic(float t)
        {
            return t * t;
        }

        public static float Out_Quadratic(float t)
        {
            return 1 - In_Quadratic(1 - t);
        }

        public static float InOut_Quadratic(float t)
        {
            if (t < 0.5f)
            {
                return In_Quadratic(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Quadratic((1f - t) * 2f) / 2;
            }
        }

        // Cubic        ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Cubic(float t)
        {
            return t * t * t;
        }

        public static float Out_Cubic(float t)
        {
            return 1 - In_Cubic(1 - t);
        }

        public static float InOut_Cubic(float t)
        {
            if (t < 0.5f)
            {
                return In_Cubic(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Cubic((1f - t) * 2f) / 2;
            }
        }

        // Quartic      ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Quartic(float t)
        {
            return Mathf.Pow(t, 4f);
        }

        public static float Out_Quartic(float t)
        {
            return 1 - In_Quartic(1 - t);
        }

        public static float InOut_Quartic(float t)
        {
            if (t < 0.5f)
            {
                return In_Quartic(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Quartic((1f - t) * 2f) / 2;
            }
        }

        // Quintic      ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Quintic(float t)
        {
            return Mathf.Pow(t, 5f);
        }

        public static float Out_Quintic(float t)
        {
            return 1 - In_Quintic(1 - t);
        }

        public static float InOut_Quintic(float t)
        {
            if (t < 0.5f)
            {
                return In_Quintic(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Quintic((1f - t) * 2f) / 2;
            }
        }

        // Bounce       ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Bounce(float t)
        {
            float p = 0.3f;
            return Mathf.Pow(2, -10 * t) * Mathf.Sin((t - p / 4) * (2 * Mathf.PI) / p) + 1;
        }

        public static float Out_Bounce(float t)
        {
            return 1 - In_Bounce(1 - t);
        }

        public static float InOut_Bounce(float t)
        {
            if (t < 0.5f)
            {
                return In_Bounce(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Bounce((1f - t) * 2f) / 2;
            }
        }

        // Sinusoidal   ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Sinusoidal(float t)
        {
            return 1 + Mathf.Sin(Mathf.PI / 2f * t - Mathf.PI / 2f);
        }

        public static float Out_Sinusoidal(float t)
        {
            return 1 - In_Sinusoidal(1 - t);
        }

        public static float InOut_Sinusoidal(float t)
        {
            if (t < 0.5f)
            {
                return In_Sinusoidal(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Sinusoidal((1f - t) * 2f) / 2;
            }
        }

        // Overhead/Back     ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Overhead(float t)
        {
            float back = 1.6f;
            return t * t * ((back + 1f) * t - back);
        }

        public static float Out_Overhead(float t)
        {
            return 1 - In_Overhead(1 - t);
        }

        public static float InOut_Overhead(float t)
        {
            if (t < 0.5f)
            {
                return In_Overhead(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Overhead((1f - t) * 2f) / 2;
            }
        }

        // Exponential  ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Exponential(float t)
        {
            return t == 0f ? 0f : Mathf.Pow(1024f, t - 1f);
        }

        public static float Out_Exponential(float t)
        {
            return 1 - In_Exponential(1 - t);
        }

        public static float InOut_Exponential(float t)
        {
            if (t < 0.5f)
            {
                return In_Exponential(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Exponential((1f - t) * 2f) / 2;
            }
        }

        // Elastic      ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Elastic(float t)
        {
            if (t == 0f) { return 0f; }
            if (t == 1f) { return 1f; }
            return -Mathf.Pow(2f, 10f * (t -= 1f)) * Mathf.Sin((t - 0.1f) * (2f * Mathf.PI) / 0.4f);
        }

        public static float Out_Elastic(float t)
        {
            return 1 - In_Elastic(1 - t);
        }

        public static float InOut_Elastic(float t)
        {
            if (t < 0.5f)
            {
                return In_Elastic(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Elastic((1f - t) * 2f) / 2;
            }
        }

        // Circular     ---------------------------------------------------------------------------------------------------------------------------

        public static float In_Circular(float t)
        {
            return 1f - Mathf.Sqrt(1f - t * t);
        }

        public static float Out_Circular(float t)
        {
            return 1 - In_Circular(1 - t);
        }

        public static float InOut_Circular(float t)
        {
            if (t < 0.5f)
            {
                return In_Circular(t * 2f) / 2f;
            }
            else
            {
                return 1 - In_Circular((1f - t) * 2f) / 2;
            }
        }
    }
}