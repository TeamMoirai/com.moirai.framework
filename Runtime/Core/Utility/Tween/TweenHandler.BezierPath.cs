using UnityEngine;

namespace Moirai.Atropos
{
    public partial class TweenHandler
    {
        #region BezierPathHelper

        /// <summary>
        /// N 阶贝塞尔曲线的计算。
        /// </summary>
        /// <param name="t"></param>
        /// <param name="points"></param>
        /// <returns></returns>
        protected static Vector3 CalculateBezierPoint(float t, Vector3[] points)
        {
            int n = points.Length - 1;
            Vector3 point = Vector3.zero;

            // 计算贝塞尔点
            for (int i = 0; i <= n; i++)
            {
                float coefficient = BinomialCoefficient(n, i) * Mathf.Pow(1 - t, n - i) * Mathf.Pow(t, i);
                point += coefficient * points[i];
            }

            return point;
        }

        /// <summary>
        /// 计算二项式系数。
        /// </summary>
        /// <param name="n"></param>
        /// <param name="k"></param>
        /// <returns></returns>
        protected static int BinomialCoefficient(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            if (k == 0 || k == n) return 1;

            int result = 1;
            for (int i = 0; i < k; i++)
            {
                result *= (n - i);
                result /= (i + 1);
            }

            return result;
        }

        #endregion
    }
}