using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class EaseUtility
    {
        // Animation curve methods --------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// 根据动画曲线对 <see cref="float"/> 值在 <paramref name="startValue"/> 与 <paramref name="endValue"/> 之间插值。
        /// </summary>
        public static float Tween(float currentTime, float initialTime, float endTime, float startValue, float endValue, AnimationCurve curve)
        {
            currentTime = MathsUtility.Remap(currentTime, initialTime, endTime, 0f, 1f);
            currentTime = curve.Evaluate(currentTime);
            return startValue + currentTime * (endValue - startValue);
        }
        
        /// <summary>
        /// 根据动画曲线对 <see cref="long"/> 值在 <paramref name="startValue"/> 与 <paramref name="endValue"/> 之间插值（结果向下取整）。
        /// </summary>
        public static long Tween(float currentTime, float initialTime, float endTime, long startValue, long endValue, AnimationCurve curve)
        {
            currentTime = MathsUtility.Remap(currentTime, initialTime, endTime, 0f, 1f);
            currentTime = curve.Evaluate(currentTime);
            float interpolatedValue = startValue + currentTime * (endValue - startValue);
            return (long)interpolatedValue;
        }

        /// <summary>
        /// 根据动画曲线对 <see cref="Vector2"/> 的 x、y 分量逐分量插值。
        /// </summary>
        public static Vector2 Tween(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue, AnimationCurve curve)
        {
            startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
            startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
            return startValue;
        }

        /// <summary>
        /// 根据动画曲线对 <see cref="Vector3"/> 的 x、y、z 分量逐分量插值。
        /// </summary>
        /// <summary>
        /// 根据动画曲线对 <see cref="Vector3"/> 的 x、y、z 分量逐分量插值。
        /// </summary>
        public static Vector3 Tween(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue, AnimationCurve curve)
        {
            startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
            startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
            startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
            return startValue;
        }

        /// <summary>
        /// 根据动画曲线对 <see cref="Vector4"/> 的 x、y、z、w 分量逐分量插值。
        /// </summary>
        public static Vector4 Tween(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue, AnimationCurve curve)
        {
            startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
            startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
            startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
            startValue.w = Tween(currentTime, initialTime, endTime, startValue.w, endValue.w, curve);
            return startValue;
        }
        
        /// <summary>
        /// 根据动画曲线的插值速率对四元数进行球面插值（Slerp）。
        /// </summary>
        public static Quaternion Tween(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue, AnimationCurve curve)
        {
            float turningRate = Tween(currentTime, initialTime, endTime, 0f, 1f, curve);
            startValue = Quaternion.Slerp(startValue, endValue, turningRate);
            return startValue;
        }
    }
}