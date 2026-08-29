using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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
    public abstract partial class TweenHandler : FrameworkHandler
    {
        [SerializeField] private float m_CheckInterval = 60f;

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化回调：注册到 TweenManager。
        /// </summary>
        protected override void OnInit()
        {
            TweenManager.EnsureInstance();
            TweenManager.Register(this);
            TweenManager.SetCheckInterval(m_CheckInterval);
        }

        /// <summary>
        /// 关闭回调：注销 TweenManager。
        /// </summary>
        protected override void OnShutdown()
        {
            TweenManager.Unregister(this);
        }

        /// <summary>
        /// 清理已失效的 Tween 缓存，同时释放底层库的缓存条目。
        /// </summary>
        public abstract void ReleaseUnusedTween();

        #endregion

        #region 基础方法 [CORE METHODS]

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

        #region 暂停与等待 [PAUSE & AWAIT]

        /// <summary>
        /// 暂停指定 tween（冻结时间推进）。
        /// 默认实现不支持暂停——抛出 <see cref="GameException"/>；实现方按需覆写。
        /// </summary>
        public virtual void Pause(long tweenId)
        {
            throw new GameException(StringUtility.Format("TweenHandler '{0}' does not implement Pause.", GetType().Name));
        }

        /// <summary>
        /// 恢复指定 tween。
        /// 默认实现不支持恢复——抛出 <see cref="GameException"/>；实现方按需覆写。
        /// </summary>
        public virtual void Resume(long tweenId)
        {
            throw new GameException(StringUtility.Format("TweenHandler '{0}' does not implement Resume.", GetType().Name));
        }

        /// <summary>
        /// 等待 tween 结束（UniTask）。
        /// <para>任何结束原因（自然完成/Complete/Stop/目标销毁/清理）→ 正常返回，不区分死因；
        /// 仅外部 CancellationToken 取消 → OperationCanceledException（放弃等待，tween 不被停止）。</para>
        /// <para>基类默认实现为逐帧轮询兜底（async Yield 循环，无闭包/无每帧委托分配，判定晚一帧）；
        /// DefaultTweenHandler / LitMotionHandler 覆写为完成信号即时版本；PrimeTweenHandler 覆写为同构轮询版。</para>
        /// </summary>
        public virtual async UniTask WaitAsync(long tweenId, CancellationToken cancellationToken = default)
        {
            while (IsAlive(tweenId))
            {
                await UniTask.Yield(cancellationToken);
            }
        }

        #endregion

        #region 延迟 [DELAY]

        public abstract long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true);

        public abstract long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true);

        #endregion

        #region Transform 补间 — LocalRotation (Vector3) [TRANSFORM — LOCAL ROTATION V3]

        public abstract long LocalRotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalRotation(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Scale (float) [TRANSFORM — SCALE FLOAT]

        public abstract long Scale(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Scale(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Rotation (Vector3) [TRANSFORM — ROTATION V3]

        public abstract long Rotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Rotation(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Position [TRANSFORM — POSITION]

        public abstract long Position(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Position(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — PositionX / Y / Z [TRANSFORM — POSITION AXIS]

        public abstract long PositionX(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionX(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionY(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionY(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionZ(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long PositionZ(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — LocalPosition [TRANSFORM — LOCAL POSITION]

        public abstract long LocalPosition(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPosition(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — LocalPositionX / Y / Z [TRANSFORM — LOCAL POSITION AXIS]

        public abstract long LocalPositionX(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionX(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionY(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionY(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionZ(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalPositionZ(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Rotation (Quaternion) [TRANSFORM — ROTATION QUAT]

        public abstract long Rotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Rotation(Transform target, Quaternion startValue, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — LocalRotation (Quaternion) [TRANSFORM — LOCAL ROTATION QUAT]

        public abstract long LocalRotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long LocalRotation(Transform target, Quaternion startValue, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — Scale (Vector3) [TRANSFORM — SCALE V3]

        public abstract long Scale(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Scale(Transform target, Vector3 startValue, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region Transform 补间 — ScaleX / Y / Z [TRANSFORM — SCALE AXIS]

        public abstract long ScaleX(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleX(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleY(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleY(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleZ(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long ScaleZ(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region SpriteRenderer / Material 补间 [SPRITE & MATERIAL]

        public abstract long Color(SpriteRenderer target, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Color(SpriteRenderer target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(SpriteRenderer target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(SpriteRenderer target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long MaterialColor(Material target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region UI 补间 [UI]

        public abstract long UISliderValue(Slider target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UISliderValue(Slider target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UINormalizedPosition(ScrollRect target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UINormalizedPosition(ScrollRect target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIHorizontalNormalizedPosition(ScrollRect target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIHorizontalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPosition(RectTransform target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPosition(RectTransform target, Vector2 startValue, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionX(RectTransform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionX(RectTransform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionY(RectTransform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIAnchoredPositionY(RectTransform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIVerticalNormalizedPosition(ScrollRect target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIVerticalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration, TweenEase ease = default,
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

        public abstract long Alpha(CanvasGroup target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(CanvasGroup target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(Graphic target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long Alpha(Graphic target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIFillAmount(Image target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        public abstract long UIFillAmount(Image target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region 贝塞尔路径 [BEZIER PATH]

        public abstract long MoveBezierPath(Transform target, Vector3[] path, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null);

        #endregion

        #region 自定义补间 [CUSTOM]

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

        /// <summary>
        /// 零分配 Custom 重载：回调直接持有 object 目标，避免泛型闭包分配。
        /// 调用侧使用 static lambda / 方法组时无任何堆分配。
        /// 默认实现回退到泛型版本；DefaultTweenHandler 覆写为直存回调（0 GC）。
        /// </summary>
        public virtual long Custom(object target, float startValue, float endValue, float duration, Action<object, float> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            // 显式泛型实参，避免重载解析回环到本方法
            return Custom<object>(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        /// <summary>
        /// 零分配 Custom（Vector3）重载：回调直接持有 object 目标，避免泛型闭包分配。
        /// </summary>
        public virtual long Custom(object target, Vector3 startValue, Vector3 endValue, float duration, Action<object, Vector3> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return Custom<object>(target, startValue, endValue, duration, onValueChange, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        #endregion
    }
}
