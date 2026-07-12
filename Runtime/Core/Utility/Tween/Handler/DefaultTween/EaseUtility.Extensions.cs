using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class EaseUtility
    {
        public static Vector2 Tween(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue, EEaseType curve)
		{
			startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
			startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
			return startValue;
		}

		public static Vector3 Tween(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue, EEaseType curve)
		{
			startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
			startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
			startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
			return startValue;
		}
		
		public static Vector4 Tween(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue, EEaseType curve)
		{
			startValue.x = Tween(currentTime, initialTime, endTime, startValue.x, endValue.x, curve);
			startValue.y = Tween(currentTime, initialTime, endTime, startValue.y, endValue.y, curve);
			startValue.z = Tween(currentTime, initialTime, endTime, startValue.z, endValue.z, curve);
			startValue.w = Tween(currentTime, initialTime, endTime, startValue.w, endValue.w, curve);
			return startValue;
		}

		public static Quaternion Tween(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue, EEaseType curve)
		{
			float turningRate = Tween(currentTime, initialTime, endTime, 0f, 1f, curve);
			startValue = Quaternion.Slerp(startValue, endValue, turningRate);
			return startValue;
		}

		// Tween type methods ------------------------------------------------------------------------------------------------------------------------

		public static float Tween(float currentTime, float initialTime, float endTime, float startValue, float endValue, Tween tween)
		{
			if (tween.TweenType == TweenTypes.Tween)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.EaseType);
			}
			if (tween.AnimationCurve != null)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.AnimationCurve);
			}
			return 0f;
		}
		public static long Tween(float currentTime, float initialTime, float endTime, long startValue, long endValue, Tween tween)
		{
			if (tween.TweenType == TweenTypes.Tween)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.EaseType);
			}
			if (tween.TweenType == TweenTypes.AnimationCurve)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.AnimationCurve);
			}
			return 0;
		}
		public static Vector2 Tween(float currentTime, float initialTime, float endTime, Vector2 startValue, Vector2 endValue, Tween tween)
		{
			if (tween.TweenType == TweenTypes.Tween)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.EaseType);
			}
			if (tween.TweenType == TweenTypes.AnimationCurve)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.AnimationCurve);
			}
			return Vector2.zero;
		}
		public static Vector3 Tween(float currentTime, float initialTime, float endTime, Vector3 startValue, Vector3 endValue, Tween tween)
		{
			if (tween.TweenType == TweenTypes.Tween)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.EaseType);
			}
			if (tween.TweenType == TweenTypes.AnimationCurve)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.AnimationCurve);
			}
			return Vector3.zero;
		}
		public static Vector4 Tween(float currentTime, float initialTime, float endTime, Vector4 startValue, Vector4 endValue, Tween tween)
		{
			if (tween.TweenType == TweenTypes.Tween)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.EaseType);
			}
			if (tween.TweenType == TweenTypes.AnimationCurve)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.AnimationCurve);
			}
			return Vector3.zero;
		}
		public static Quaternion Tween(float currentTime, float initialTime, float endTime, Quaternion startValue, Quaternion endValue, Tween tween)
		{
			if (tween.TweenType == TweenTypes.Tween)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.EaseType);
			}
			if (tween.TweenType == TweenTypes.AnimationCurve)
			{
				return Tween(currentTime, initialTime, endTime, startValue, endValue, tween.AnimationCurve);
			}
			return Quaternion.identity;
		}
    }
}