using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class EaseUtility
    {
        public static Vector2 Tween(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue, TweenUtility.EEase curve)
		{
			startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
			startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
			return startValue;
		}

		public static Vector3 Tween(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue, TweenUtility.EEase curve)
		{
			startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
			startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
			startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
			return startValue;
		}
		
		public static Vector4 Tween(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue, TweenUtility.EEase curve)
		{
			startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
			startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
			startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
			startValue.w = Tween(currentTime, initialTime, endTime, startValue.w, endValue.w, curve);
			return startValue;
		}

		public static Quaternion Tween(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue, TweenUtility.EEase curve)
		{
			float turningRate = Tween(currentTime, initialTime, endTime, 0f, 1f, curve);
			startValue = Quaternion.Slerp(startValue, endValue, turningRate);
			return startValue;
		}

		// Tween type methods ------------------------------------------------------------------------------------------------------------------------

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