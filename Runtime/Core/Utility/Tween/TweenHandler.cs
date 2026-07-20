using System;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos
{
    /// <summary>
    /// 缓动动画处理器抽象基类。
    /// <para>
    /// 实现方可为 PrimeTween、LitMotion、DOTween 或自研引擎。
    /// 所有缓动方法统一接收 <see cref="TweenEase"/>，
    /// 实现方可通过 <see cref="TweenEase.IsCurve"/> / <see cref="TweenEase.IsEase"/>
    /// 判断并转换为自身格式，也可直接调用 <see cref="TweenEase.Evaluate(float)"/>。
    /// </para>
    /// </summary>
    [Serializable]
    public abstract partial class TweenHandler
    {
        [SerializeField] private float m_CheckInterval = 60f;

        #region 生命周期

        /// <summary>
        /// 内部初始化入口。注册到 TweenManager 后调用子类 OnInit。
        /// </summary>
        internal void Internal_Init()
        {
            TweenManager.EnsureInstance();
            TweenManager.Register(this);
            TweenManager.SetCheckInterval(m_CheckInterval);
            OnInit();
        }

        /// <summary>
        /// 内部初始化入口。注册到 TweenManager 后调用子类 OnInit。
        /// </summary>
        internal void Internal_Shutdown()
        {
            TweenManager.Unregister(this);
            Shutdown();
        }

        /// <summary>
        /// 清理已失效的 Tween 缓存，同时释放底层库的缓存条目。
        /// </summary>
        public abstract void ReleaseUnusedTween();

        #endregion

        #region 基础方法

        protected abstract void OnInit();

        protected abstract void Shutdown();

        /// <summary>
        /// 判断指定对象是否正在执行Tween动画。
        /// </summary>
        /// <param name="onTarget">需要检查的对象。</param>
        /// <returns>如果正在执行Tween动画则返回true，否则返回false。</returns>
        // ReSharper disable once IdentifierTypo
        public abstract bool IsTweening(object onTarget);

        /// <summary>
        /// 获取指定对象正在执行的Tween动画数量。
        /// </summary>
        /// <param name="onTarget">需要检查的对象。</param>
        /// <returns>正在执行的Tween动画数量。</returns>
        public abstract int GetTweenCount(object onTarget);

        /// <summary>
        /// 判断指定ID的Tween是否还存活。
        /// </summary>
        /// <param name="tweenId">Tween的ID。</param>
        /// <returns>如果Tween还存活则返回true，否则返回false。</returns>
        public abstract bool IsAlive(long tweenId);

        /// <summary>
        /// 立即停止指定缓动。
        /// </summary>
        public abstract void Stop(long tweenId);

        /// <summary>
        /// 立即完成指定缓动（跳到终值）。
        /// </summary>
        public abstract void Complete(long tweenId);

        /// <summary>
        /// 停止目标上所有缓动。target 为 null 时停止全部。返回停止的数量。
        /// </summary>
        public abstract int StopAll(object onTarget = null);

        /// <summary>
        /// 完成目标上所有缓动。target 为 null 时完成全部。返回完成的数量。
        /// </summary>
        public abstract int CompleteAll(object onTarget = null);

        #endregion

        #region Delay

        public abstract long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true);

        public abstract long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true);

        #endregion

        #region Transform 补间 — LocalRotation (Vector3)

        public abstract long LocalRotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalRotation(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Scale (float)

        public abstract long Scale(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Scale(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Rotation (Vector3)

        public abstract long Rotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Rotation(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Position

        public abstract long Position(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Position(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — PositionX / Y / Z

        public abstract long PositionX(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionX(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionY(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionY(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionZ(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionZ(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — LocalPosition

        public abstract long LocalPosition(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPosition(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — LocalPositionX / Y / Z

        public abstract long LocalPositionX(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionX(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionY(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionY(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionZ(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionZ(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Rotation (Quaternion)

        public abstract long Rotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Rotation(Transform target, Quaternion startValue, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — LocalRotation (Quaternion)

        public abstract long LocalRotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalRotation(Transform target, Quaternion startValue, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Scale (Vector3)

        public abstract long Scale(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Scale(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — ScaleX / Y / Z

        public abstract long ScaleX(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleX(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleY(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleY(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleZ(Transform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleZ(Transform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region SpriteRenderer / Material 补间

        public abstract long Color(SpriteRenderer target, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Color(SpriteRenderer target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(SpriteRenderer target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(SpriteRenderer target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long MaterialColor(Material target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region UI 补间

        public abstract long UISliderValue(Slider target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UISliderValue(Slider target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UINormalizedPosition(ScrollRect target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UINormalizedPosition(ScrollRect target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIHorizontalNormalizedPosition(ScrollRect target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIHorizontalNormalizedPosition(ScrollRect target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPosition(RectTransform target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPosition(RectTransform target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionX(RectTransform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionX(RectTransform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionY(RectTransform target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionY(RectTransform target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIVerticalNormalizedPosition(ScrollRect target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIVerticalNormalizedPosition(ScrollRect target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPosition3D(RectTransform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPosition3D(RectTransform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UISizeDelta(RectTransform target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UISizeDelta(RectTransform target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Color(Graphic target, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Color(Graphic target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(CanvasGroup target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(CanvasGroup target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(Graphic target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(Graphic target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIFillAmount(Image target, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIFillAmount(Image target, Single startValue, Single endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Bezier Path

        public abstract long MoveBezierPath(Transform target, Vector3[] path, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Custom

        public abstract long Custom<T>(T target, Vector3 startValue, Vector3 endValue, float duration, Action<T, Vector3> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class;

        public abstract long Custom<T>(T target, int startValue, int endValue, float duration, Action<T, int> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class;

        public abstract long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class;

        public abstract long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
            where T : class;

        #endregion
    }
}
