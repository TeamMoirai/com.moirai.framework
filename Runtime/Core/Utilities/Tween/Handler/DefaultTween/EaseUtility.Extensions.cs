using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class EaseUtility
    {
        /// <summary>
        /// 按指定缓动曲线对 <see cref="Vector2"/> 的 x、y 分量逐分量插值。
        /// </summary>
        public static Vector2 Tween(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue, TweenUtility.EEase curve)
        {
            startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
            startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
            return startValue;
        }

        /// <summary>
        /// 按指定缓动曲线对 <see cref="Vector3"/> 的 x、y、z 分量逐分量插值。
        /// </summary>
        public static Vector3 Tween(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue, TweenUtility.EEase curve)
        {
            startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
            startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
            startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
            return startValue;
        }
        
        /// <summary>
        /// 按指定缓动曲线对 <see cref="Vector4"/> 的 x、y、z、w 分量逐分量插值。
        /// </summary>
        public static Vector4 Tween(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue, TweenUtility.EEase curve)
        {
            startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
            startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
            startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
            startValue.w = Tween(currentTime, initialTime, endTime, startValue.w, endValue.w, curve);
            return startValue;
        }

        /// <summary>
        /// 按指定缓动曲线对四元数进行球面插值（Slerp）。
        /// </summary>
        public static Quaternion Tween(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue, TweenUtility.EEase curve)
        {
            float turningRate = Tween(currentTime, initialTime, endTime, 0f, 1f, curve);
            startValue = Quaternion.Slerp(startValue, endValue, turningRate);
            return startValue;
        }

        // Tween type methods ------------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// 按补间缓动配置对 <see cref="float"/> 值插值：配置为缓动曲线或动画曲线时走对应实现，均未配置返回 0。
        /// </summary>
        public static float Tween(float currentTime, float initialTime, float endTime, float startValue, float endValue, TweenEase tweenEase)
        {
            if (tweenEase.TweenType == TweenEase.ETweenType.Ease)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.EaseType);
            }
            if (tweenEase.AnimationCurve != null)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.AnimationCurve);
            }
            return 0f;
        }
        /// <summary>
        /// 按补间缓动配置对 <see cref="long"/> 值插值：配置为缓动曲线或动画曲线时走对应实现，均未配置返回 0。
        /// </summary>
        public static long Tween(float currentTime, float initialTime, float endTime, long startValue, long endValue, TweenEase tweenEase)
        {
            if (tweenEase.TweenType == TweenEase.ETweenType.Ease)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.EaseType);
            }
            if (tweenEase.TweenType == TweenEase.ETweenType.AnimationCurve)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.AnimationCurve);
            }
            return 0;
        }
        /// <summary>
        /// 按补间缓动配置对 <see cref="Vector2"/> 值插值：配置为缓动曲线或动画曲线时走对应实现，均未配置返回零向量。
        /// </summary>
        public static Vector2 Tween(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue, TweenEase tweenEase)
        {
            if (tweenEase.TweenType == TweenEase.ETweenType.Ease)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.EaseType);
            }
            if (tweenEase.TweenType == TweenEase.ETweenType.AnimationCurve)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.AnimationCurve);
            }
            return Vector2.zero;
        }
        /// <summary>
        /// 按补间缓动配置对 <see cref="Vector3"/> 值插值：配置为缓动曲线或动画曲线时走对应实现，均未配置返回零向量。
        /// </summary>
        public static Vector3 Tween(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue, TweenEase tweenEase)
        {
            if (tweenEase.TweenType == TweenEase.ETweenType.Ease)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.EaseType);
            }
            if (tweenEase.TweenType == TweenEase.ETweenType.AnimationCurve)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.AnimationCurve);
            }
            return Vector3.zero;
        }
        /// <summary>
        /// 按补间缓动配置对 <see cref="Vector4"/> 值插值：配置为缓动曲线或动画曲线时走对应实现，均未配置返回零向量。
        /// </summary>
        public static Vector4 Tween(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue, TweenEase tweenEase)
        {
            if (tweenEase.TweenType == TweenEase.ETweenType.Ease)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.EaseType);
            }
            if (tweenEase.TweenType == TweenEase.ETweenType.AnimationCurve)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.AnimationCurve);
            }
            return Vector3.zero;
        }
        /// <summary>
        /// 按补间缓动配置对四元数插值：配置为缓动曲线或动画曲线时走对应实现，均未配置返回单位四元数。
        /// </summary>
        public static Quaternion Tween(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue, TweenEase tweenEase)
        {
            if (tweenEase.TweenType == TweenEase.ETweenType.Ease)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.EaseType);
            }
            if (tweenEase.TweenType == TweenEase.ETweenType.AnimationCurve)
            {
                return Tween(currentTime, initialTime, endTime, startValue, endValue, tweenEase.AnimationCurve);
            }
            return Quaternion.identity;
        }
    }
}