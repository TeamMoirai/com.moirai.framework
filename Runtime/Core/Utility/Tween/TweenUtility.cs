using System;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos
{
    /// <summary>
    /// 缓动动画统一门面（Facade）。
    /// </summary>
    /// <para>
    /// 所有缓动参数类型为 <see cref="TweenEase"/>，支持隐式转换：
    /// <code>
    /// TweenUtility.Position(t, end, 0.3f); // 默认 Linear
    /// TweenUtility.Position(t, end, 0.3f, TweenUtility.EEase.OutQuad); // 枚举缓动
    /// TweenUtility.Position(t, end, 0.3f, myAnimationCurve); // 曲线缓动
    /// </code>
    /// </para>
    public static partial class TweenUtility
    {
        private static TweenHandler s_Handler = null;
        /// <summary>
        /// 获取/设置缓动动画处理器。
        /// </summary>
        public static TweenHandler Handler
        {
            get
            {
                if (s_Handler == null) Handler = new DefaultTweenHandler();
                return s_Handler;
            }
            set
            {
                if (s_Handler == value || value == null) return;

                s_Handler?.Internal_Shutdown();
                s_Handler = value;
                s_Handler.Internal_Init();
            }
        }

        // ReSharper disable once IdentifierTypo
        public static bool IsTweening(object onTarget)
        {
            return Handler.IsTweening(onTarget);
        }

        public static int GetTweenCount(object onTarget)
        {
            return Handler.GetTweenCount(onTarget);
        }

        public static bool IsAlive(long tweenId)
        {
            return Handler.IsAlive(tweenId);
        }

        public static void Stop(long tweenId)
        {
            Handler.Stop(tweenId);
        }

        public static void Complete(long tweenId)
        {
            Handler.Complete(tweenId);
        }

        public static int StopAll(object onTarget = null)
        {
            return Handler.StopAll(onTarget);
        }

        public static int CompleteAll(object onTarget = null)
        {
            return Handler.CompleteAll(onTarget);
        }
        
        public static long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            return Handler.Delay(duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
        }

        public static long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            return Handler.Delay(target, duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
        }

        public static long LocalRotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalRotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long LocalRotation(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalRotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Scale(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Scale(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Scale(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Scale(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Rotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Rotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Rotation(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Rotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Position(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Position(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Position(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Position(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long PositionX(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.PositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long PositionX(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.PositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long PositionY(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.PositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long PositionY(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.PositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long PositionZ(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.PositionZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long PositionZ(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.PositionZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long LocalPosition(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long LocalPosition(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long LocalPositionX(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long LocalPositionX(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long LocalPositionY(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long LocalPositionY(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long LocalPositionZ(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPositionZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long LocalPositionZ(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalPositionZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Rotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Rotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Rotation(Transform target, Quaternion startValue, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Rotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long LocalRotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalRotation(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long LocalRotation(Transform target, Quaternion startValue, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.LocalRotation(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Scale(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Scale(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Scale(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Scale(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long ScaleX(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.ScaleX(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long ScaleX(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.ScaleX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long ScaleY(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.ScaleY(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long ScaleY(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.ScaleY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long ScaleZ(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.ScaleZ(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long ScaleZ(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.ScaleZ(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Color(SpriteRenderer target, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Color(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Color(SpriteRenderer target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Color(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Alpha(SpriteRenderer target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Alpha(SpriteRenderer target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UISliderValue(Slider target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UISliderValue(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UISliderValue(Slider target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UISliderValue(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UINormalizedPosition(ScrollRect target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UINormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UINormalizedPosition(ScrollRect target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UINormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UIHorizontalNormalizedPosition(ScrollRect target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIHorizontalNormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIHorizontalNormalizedPosition(ScrollRect target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIHorizontalNormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UIAnchoredPosition(RectTransform target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIAnchoredPosition(RectTransform target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIAnchoredPositionX(RectTransform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPositionX(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIAnchoredPositionX(RectTransform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPositionX(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UIAnchoredPositionY(RectTransform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPositionY(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIAnchoredPositionY(RectTransform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPositionY(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UIVerticalNormalizedPosition(ScrollRect target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIVerticalNormalizedPosition(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIVerticalNormalizedPosition(ScrollRect target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIVerticalNormalizedPosition(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UIAnchoredPosition3D(RectTransform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPosition3D(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIAnchoredPosition3D(RectTransform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIAnchoredPosition3D(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UISizeDelta(RectTransform target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UISizeDelta(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UISizeDelta(RectTransform target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UISizeDelta(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Color(Graphic target, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Color(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Color(Graphic target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Color(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Alpha(CanvasGroup target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Alpha(CanvasGroup target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long Alpha(Graphic target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Alpha(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Alpha(Graphic target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.Alpha(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }


        public static long UIFillAmount(Image target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIFillAmount(target, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long UIFillAmount(Image target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.UIFillAmount(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long MaterialColor(Material target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.MaterialColor(target, startValue, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long MoveBezierPath(Transform target, Vector3[] path, float duration, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Handler.MoveBezierPath(target, path, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Custom<T>(T target, Vector3 startValue, Vector3 endValue, float duration, Action<T, Vector3> onValueChange, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class
        {
            return Handler.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class
        {
            return Handler.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Custom<T>(T target, int startValue, int endValue, float duration, Action<T, int> onValueChange, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class
        {
            return Handler.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public static long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange, TweenEase ease = default,
            int cycles = 1, ECycleMode cycleMode = ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class
        {
            return Handler.Custom(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }
    }
}