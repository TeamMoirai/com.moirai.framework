using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
	public static partial class EaseUtility
    {
        // ASYNC MOVE METHODS ---------------------------------------------------------------------------------------------------------
		
		public static async UniTask MoveTransformAsync(Transform targetTransform, Vector3 origin, Vector3 destination, float delayDuration, float duration, TweenUtility.EEase curve, bool ignoreTimescale = false)
		{
		    if (delayDuration > 0f)
		    {
		        await UniTask.Delay((int)(delayDuration * 1000), ignoreTimeScale: ignoreTimescale);
		    }

		    float timeLeft = duration;
		    while (timeLeft > 0f)
		    {
		        targetTransform.position = Tween(duration - timeLeft, 0f, duration, origin, destination, curve);
		        timeLeft -= ignoreTimescale ? Time.unscaledDeltaTime : Time.deltaTime;
		        await UniTask.Yield();
		    }

		    targetTransform.position = destination;
		}

		public static async UniTask MoveRectTransformAsync(RectTransform targetTransform, Vector3 origin, Vector3 destination, float delayDuration, float duration, TweenUtility.EEase curve, bool ignoreTimescale = false)
		{
		    if (delayDuration > 0f)
		    {
		        await UniTask.Delay((int)(delayDuration * 1000), ignoreTimeScale: ignoreTimescale);
		    }

		    float timeLeft = duration;
		    while (timeLeft > 0f)
		    {
		        targetTransform.localPosition = Tween(duration - timeLeft, 0f, duration, origin, destination, curve);
		        timeLeft -= ignoreTimescale ? Time.unscaledDeltaTime : Time.deltaTime;
		        await UniTask.Yield();
		    }

		    targetTransform.localPosition = destination;
		}

		public static async UniTask MoveTransformAsync(Transform targetTransform, Transform origin, Transform destination, float delayDuration, float duration, TweenUtility.EEase curve, bool updatePosition = true, bool updateRotation = true, bool ignoreTimescale = false)
	    {
	        if (delayDuration > 0f)
	        {
	            await UniTask.Delay((int)(delayDuration * 1000), ignoreTimeScale: ignoreTimescale);
	        }

	        float timeLeft = duration;
	        while (timeLeft > 0f)
	        {
	            if (updatePosition)
	            {
	                targetTransform.position = Tween(duration - timeLeft, 0f, duration, origin.position, destination.position, curve);
	            }
	            if (updateRotation)
	            {
	                targetTransform.rotation = Tween(duration - timeLeft, 0f, duration, origin.rotation, destination.rotation, curve);
	            }
	            timeLeft -= ignoreTimescale ? Time.unscaledDeltaTime : Time.deltaTime;
	            await UniTask.Yield();
	        }

	        if (updatePosition) { targetTransform.position = destination.position; }
	        if (updateRotation) { targetTransform.localEulerAngles = destination.localEulerAngles; }
	    }
		
	    public static async UniTask RotateTransformAroundAsync(Transform targetTransform, Transform center, Transform destination, float angle, float delayDuration, float duration, TweenUtility.EEase curve, bool ignoreTimescale = false)
	    {
	        if (delayDuration > 0f)
	        {
	            await UniTask.Delay((int)(delayDuration * 1000), ignoreTimeScale: ignoreTimescale);
	        }

	        Vector3 initialRotationPosition = targetTransform.position;
	        
	        float timeSpent = 0f;
	        while (timeSpent < duration)
	        {
	            float newAngle = Tween(timeSpent, 0f, duration, 0f, angle, curve);

	            targetTransform.position = initialRotationPosition;
	            Quaternion initialRotationRotation = targetTransform.rotation;
	            targetTransform.RotateAround(center.position, center.up, newAngle);
	            targetTransform.rotation = initialRotationRotation;

	            timeSpent += ignoreTimescale ? Time.unscaledDeltaTime : Time.deltaTime;
	            await UniTask.Yield();
	        }
	        targetTransform.position = destination.position;
	    }
	    
	    // ASYNC UI METHODS ---------------------------------------------------------------------------------------------------------
	    
	    public static async UniTask CrossFadeAlpha(CanvasGroup canvasGroup, float origin, float destination, float duration, TweenUtility.EEase curve, bool ignoreTimescale = false)
	    {
		    float timeLeft = duration;
		    while (timeLeft > 0f)
		    {
			    canvasGroup.alpha = Tween(duration - timeLeft, 0f, origin, 0f, destination, curve);
			    timeLeft -= ignoreTimescale ? Time.unscaledDeltaTime : Time.deltaTime;
			    await UniTask.Yield();
		    }
	    }
    }
}