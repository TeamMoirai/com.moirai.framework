using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class EaseUtility
    {
        // ASYNC MOVE METHODS ---------------------------------------------------------------------------------------------------------
        
        /// <summary>
        /// 异步移动 <see cref="Transform"/> 的世界坐标，从 <paramref name="origin"/> 缓动到 <paramref name="destination"/>。
        /// </summary>
        /// <param name="targetTransform">目标变换。</param>
        /// <param name="origin">起始世界坐标。</param>
        /// <param name="destination">目标世界坐标。</param>
        /// <param name="delayDuration">开始前的延迟时长（秒）。</param>
        /// <param name="duration">补间时长（秒）。</param>
        /// <param name="curve">缓动曲线类型。</param>
        /// <param name="ignoreTimescale">是否忽略时间缩放（使用 unscaledDeltaTime）。</param>
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

        /// <summary>
        /// 异步移动 <see cref="RectTransform"/> 的本地坐标（localPosition），从 <paramref name="origin"/> 缓动到 <paramref name="destination"/>。
        /// </summary>
        /// <param name="targetTransform">目标 UI 变换。</param>
        /// <param name="origin">起始本地坐标。</param>
        /// <param name="destination">目标本地坐标。</param>
        /// <param name="delayDuration">开始前的延迟时长（秒）。</param>
        /// <param name="duration">补间时长（秒）。</param>
        /// <param name="curve">缓动曲线类型。</param>
        /// <param name="ignoreTimescale">是否忽略时间缩放（使用 unscaledDeltaTime）。</param>
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

        /// <summary>
        /// 异步将 <see cref="Transform"/> 从 <paramref name="origin"/> 变换移动/旋转到 <paramref name="destination"/> 变换，可分别控制是否更新位置与旋转。
        /// </summary>
        /// <param name="targetTransform">目标变换。</param>
        /// <param name="origin">起始参照变换。</param>
        /// <param name="destination">目标参照变换。</param>
        /// <param name="delayDuration">开始前的延迟时长（秒）。</param>
        /// <param name="duration">补间时长（秒）。</param>
        /// <param name="curve">缓动曲线类型。</param>
        /// <param name="updatePosition">是否插值更新位置。</param>
        /// <param name="updateRotation">是否插值更新旋转。</param>
        /// <param name="ignoreTimescale">是否忽略时间缩放（使用 unscaledDeltaTime）。</param>
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
        
        /// <summary>
        /// 异步将 <see cref="Transform"/> 绕 <paramref name="center"/> 沿其 up 轴旋转指定角度，结束后将位置设为 <paramref name="destination"/> 的位置。
        /// </summary>
        /// <param name="targetTransform">目标变换。</param>
        /// <param name="center">旋转中心参照变换（使用其位置与 up 轴）。</param>
        /// <param name="destination">结束后的目标位置参照变换。</param>
        /// <param name="angle">总旋转角度（度）。</param>
        /// <param name="delayDuration">开始前的延迟时长（秒）。</param>
        /// <param name="duration">补间时长（秒）。</param>
        /// <param name="curve">缓动曲线类型。</param>
        /// <param name="ignoreTimescale">是否忽略时间缩放（使用 unscaledDeltaTime）。</param>
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
        
        /// <summary>
        /// 异步渐变 <see cref="CanvasGroup"/> 的透明度，从 <paramref name="origin"/> 缓动到 <paramref name="destination"/>。
        /// </summary>
        /// <param name="canvasGroup">目标画布组。</param>
        /// <param name="origin">起始透明度（0-1）。</param>
        /// <param name="destination">目标透明度（0-1）。</param>
        /// <param name="duration">补间时长（秒）。</param>
        /// <param name="curve">缓动曲线类型。</param>
        /// <param name="ignoreTimescale">是否忽略时间缩放（使用 unscaledDeltaTime）。</param>
        public static async UniTask CrossFadeAlpha(CanvasGroup canvasGroup, float origin, float destination, float duration, TweenUtility.EEase curve, bool ignoreTimescale = false)
        {
            float timeLeft = duration;
            while (timeLeft > 0f)
            {
                canvasGroup.alpha = Tween(duration - timeLeft, 0f, duration, origin, destination, curve);
                timeLeft -= ignoreTimescale ? Time.unscaledDeltaTime : Time.deltaTime;
                await UniTask.Yield();
            }
        }
    }
}