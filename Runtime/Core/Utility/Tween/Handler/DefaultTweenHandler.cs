using System;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认补间动画处理器。
    /// </summary>
    [Serializable]
    public sealed class DefaultTweenHandler : TweenHandler
    {
        #region 基础方法

        protected override void OnInit()
        {
            throw new NotImplementedException();
        }

        protected override void Shutdown()
        {
            throw new NotImplementedException();
        }

        public override void ReleaseUnusedTween()
        {
            throw new NotImplementedException();
        }

        public override bool IsTweening(object onTarget)
        {
            throw new NotImplementedException();
        }

        public override int GetTweenCount(object onTarget)
        {
            throw new NotImplementedException();
        }

        public override bool IsAlive(long tweenId)
        {
            throw new NotImplementedException();
        }

        public override void Stop(long tweenId)
        {
            throw new NotImplementedException();
        }

        public override void Complete(long tweenId)
        {
            throw new NotImplementedException();
        }

        public override int StopAll(object onTarget = null)
        {
            throw new NotImplementedException();
        }

        public override int CompleteAll(object onTarget = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Delay

        public override long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            throw new NotImplementedException();
        }

        public override long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false,
            bool warnIfTargetDestroyed = true)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — LocalRotation (Vector3)

        public override long LocalRotation(Transform target, Vector3 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalRotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — Scale (float)

        public override long Scale(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Scale(Transform target, float startValue, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — Rotation (Vector3)

        public override long Rotation(Transform target, Vector3 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Rotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — Position

        public override long Position(Transform target, Vector3 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Position(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — PositionX / Y / Z

        public override long PositionX(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long PositionX(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long PositionY(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long PositionY(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long PositionZ(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long PositionZ(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — LocalPosition

        public override long LocalPosition(Transform target, Vector3 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalPosition(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — LocalPositionX / Y / Z

        public override long LocalPositionX(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalPositionX(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalPositionY(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalPositionY(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalPositionZ(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalPositionZ(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — Rotation (Quaternion)

        public override long Rotation(Transform target, Quaternion endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Rotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — LocalRotation (Quaternion)

        public override long LocalRotation(Transform target, Quaternion endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long LocalRotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — Scale (Vector3)

        public override long Scale(Transform target, Vector3 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Scale(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Transform 补间 — ScaleX / Y / Z

        public override long ScaleX(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long ScaleX(Transform target, float startValue, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long ScaleY(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long ScaleY(Transform target, float startValue, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long ScaleZ(Transform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long ScaleZ(Transform target, float startValue, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region SpriteRenderer / Material 补间

        public override long Color(SpriteRenderer target, Color endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Color(SpriteRenderer target, Color startValue, Color endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Alpha(SpriteRenderer target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Alpha(SpriteRenderer target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long MaterialColor(Material target, Color startValue, Color endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region UI 补间

        public override long UISliderValue(Slider target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UISliderValue(Slider target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 startValue, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPositionX(RectTransform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPositionX(RectTransform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPositionY(RectTransform target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPositionY(RectTransform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UISizeDelta(RectTransform target, Vector2 endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UISizeDelta(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Color(Graphic target, Color endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Color(Graphic target, Color startValue, Color endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Alpha(CanvasGroup target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Alpha(CanvasGroup target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Alpha(Graphic target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Alpha(Graphic target, float startValue, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIFillAmount(Image target, float endValue, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long UIFillAmount(Image target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Bezier Path

        public override long MoveBezierPath(Transform target, Vector3[] path, float duration, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Custom

        public override long Custom<T>(T target, Vector3 startValue, Vector3 endValue, float duration, Action<T, Vector3> onValueChange,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Custom<T>(T target, int startValue, int endValue, float duration, Action<T, int> onValueChange,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        public override long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
