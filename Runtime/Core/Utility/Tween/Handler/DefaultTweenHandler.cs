using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认补间动画处理器。基于结构体数组 + 版本号ID实现，稳态 0 GC、高性能。
    /// <para>
    /// 语义契约（商业库对齐）：
    /// <list type="bullet">
    /// <item>自然完成 / <see cref="Complete"/>：应用终值并触发 OnComplete；</item>
    /// <item><see cref="Stop"/>：中断，不触发 OnComplete；</item>
    /// <item>目标先于补间销毁：中断（kill），不触发 OnComplete，
    /// <paramref name="warnIfTargetDestroyed"/> 控制是否记录告警。</item>
    /// </list>
    /// </para>
    /// <para>单例状态机：所有实例共享 <see cref="TweenTask"/> 静态状态，运行期仅应存在一个活跃实例。</para>
    /// </summary>
    [Serializable]
    public sealed partial class DefaultTweenHandler : TweenHandler
    {
        #region 生命周期 [LIFECYCLE]

        protected override void OnInit()
        {
            UnityUtility.AddUpdateListener(TweenTask.Update);
        }

        protected override void Shutdown()
        {
            UnityUtility.RemoveUpdateListener(TweenTask.Update);
            TweenTask.StopAll(null);
        }

        public override void ReleaseUnusedTween()
        {
            TweenTask.ReleaseUnused();
        }

        public override bool IsTweening(object onTarget)
        {
            return TweenTask.IsTweening(onTarget);
        }

        public override int GetTweenCount(object onTarget)
        {
            return TweenTask.GetTweenCount(onTarget);
        }

        public override bool IsAlive(long tweenId)
        {
            return TweenTask.IsAlive(tweenId);
        }

        public override void Stop(long tweenId)
        {
            TweenTask.Stop(tweenId);
        }

        public override void Complete(long tweenId)
        {
            TweenTask.Complete(tweenId);
        }

        public override int StopAll(object onTarget = null)
        {
            return TweenTask.StopAll(onTarget);
        }

        public override int CompleteAll(object onTarget = null)
        {
            return TweenTask.CompleteAll(onTarget);
        }

        public override void Pause(long tweenId)
        {
            TweenTask.Pause(tweenId);
        }

        public override void Resume(long tweenId)
        {
            TweenTask.Resume(tweenId);
        }

        public override UniTask WaitAsync(long tweenId, CancellationToken cancellationToken)
        {
            return TweenTask.WaitAsync(tweenId, cancellationToken);
        }

        #endregion

        #region 状态装配 [STATE BUILDING]

        /// <summary>
        /// 公共字段集中装配：消除 40+ 补间方法中重复的样板赋值。
        /// 值字段（Start/End 等）由调用方按操作类型补齐。
        /// </summary>
        private static TweenState BuildState(object target, UnityEngine.Object unityObject,
            TweenOperationType operationType, float duration, TweenEase ease, int cycles,
            TweenUtility.ECycleMode cycleMode, float startDelay, bool useUnscaledTime,
            Action onComplete, bool warnIfTargetDestroyed = false)
        {
            return new TweenState
            {
                Target = target,
                UnityObject = unityObject,
                OperationType = operationType,
                Duration = duration,
                Ease = ease,
                Cycles = cycles,
                CycleMode = cycleMode,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                UseUnscaledTime = useUnscaledTime,
                OnComplete = onComplete,
                WarnIfTargetDestroyed = warnIfTargetDestroyed,
            };
        }

        #endregion

        #region 延迟 [DELAY]

        public override long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false,
            bool warnIfTargetDestroyed = true)
        {
            return CreateDelay(null, duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
        }

        public override long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false,
            bool warnIfTargetDestroyed = true)
        {
            return CreateDelay(target, duration, onComplete, useUnscaledTime, warnIfTargetDestroyed);
        }

        private static long CreateDelay(object target, float duration, Action onComplete, bool useUnscaledTime,
            bool warnIfTargetDestroyed)
        {
            var state = BuildState(target, target as UnityEngine.Object, TweenOperationType.Delay,
                duration, default, 1, TweenUtility.ECycleMode.Restart, 0f, useUnscaledTime, onComplete,
                warnIfTargetDestroyed);
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Position [TRANSFORM — POSITION]

        public override long Position(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return Position(target, target.position, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Position(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.Position, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — PositionX / Y / Z [TRANSFORM — POSITION AXIS]

        public override long PositionX(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return PositionX(target, target.position.x, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long PositionX(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.PositionX, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            return TweenTask.Create(in state);
        }

        public override long PositionY(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return PositionY(target, target.position.y, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long PositionY(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.PositionY, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartY = startValue;
            state.EndY = endValue;
            return TweenTask.Create(in state);
        }

        public override long PositionZ(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return PositionZ(target, target.position.z, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long PositionZ(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.PositionZ, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartZ = startValue;
            state.EndZ = endValue;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalPosition [TRANSFORM — LOCAL POSITION]

        public override long LocalPosition(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return LocalPosition(target, target.localPosition, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long LocalPosition(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.LocalPosition, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalPositionX / Y / Z [TRANSFORM — LOCAL POSITION AXIS]

        public override long LocalPositionX(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return LocalPositionX(target, target.localPosition.x, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long LocalPositionX(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.LocalPositionX, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            return TweenTask.Create(in state);
        }

        public override long LocalPositionY(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return LocalPositionY(target, target.localPosition.y, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long LocalPositionY(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.LocalPositionY, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartY = startValue;
            state.EndY = endValue;
            return TweenTask.Create(in state);
        }

        public override long LocalPositionZ(Transform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return LocalPositionZ(target, target.localPosition.z, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long LocalPositionZ(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.LocalPositionZ, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartZ = startValue;
            state.EndZ = endValue;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Rotation (Vector3) [TRANSFORM — ROTATION V3]

        public override long Rotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return Rotation(target, target.eulerAngles, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Rotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.RotationVec3, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalRotation (Vector3) [TRANSFORM — LOCAL ROTATION V3]

        public override long LocalRotation(Transform target, Vector3 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return LocalRotation(target, target.localEulerAngles, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long LocalRotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.LocalRotationVec3, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Rotation (Quaternion) [TRANSFORM — ROTATION QUAT]

        public override long Rotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return Rotation(target, target.rotation, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Rotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.RotationQuat, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z; state.StartExtra = startValue.w;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z; state.EndExtra = endValue.w;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalRotation (Quaternion) [TRANSFORM — LOCAL ROTATION QUAT]

        public override long LocalRotation(Transform target, Quaternion endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return LocalRotation(target, target.localRotation, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long LocalRotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.LocalRotationQuat, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z; state.StartExtra = startValue.w;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z; state.EndExtra = endValue.w;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Scale (float) [TRANSFORM — SCALE FLOAT]

        public override long Scale(Transform target, float endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return Scale(target, target.localScale.x, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Scale(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.ScaleFloat, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Scale (Vector3) [TRANSFORM — SCALE V3]

        public override long Scale(Transform target, Vector3 endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return Scale(target, target.localScale, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Scale(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.ScaleVec3, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — ScaleX / Y / Z [TRANSFORM — SCALE AXIS]

        public override long ScaleX(Transform target, float endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return ScaleX(target, target.localScale.x, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long ScaleX(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.ScaleX, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            return TweenTask.Create(in state);
        }

        public override long ScaleY(Transform target, float endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return ScaleY(target, target.localScale.y, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long ScaleY(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.ScaleY, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartY = startValue;
            state.EndY = endValue;
            return TweenTask.Create(in state);
        }

        public override long ScaleZ(Transform target, float endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return ScaleZ(target, target.localScale.z, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long ScaleZ(Transform target, float startValue, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.ScaleZ, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartZ = startValue;
            state.EndZ = endValue;
            return TweenTask.Create(in state);
        }

        #endregion

        #region SpriteRenderer / Material 补间 [SPRITE & MATERIAL]

        public override long Color(SpriteRenderer target, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return Color(target, target.color, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Color(SpriteRenderer target, Color startValue, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.SpriteColor, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartColor = startValue;
            state.EndColor = endValue;
            return TweenTask.Create(in state);
        }

        public override long Alpha(SpriteRenderer target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return Alpha(target, target.color.a, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Alpha(SpriteRenderer target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.SpriteAlpha, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartExtra = startValue;
            state.EndExtra = endValue;
            return TweenTask.Create(in state);
        }

        public override long MaterialColor(Material target, Color startValue, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.MaterialColor, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartColor = startValue;
            state.EndColor = endValue;
            return TweenTask.Create(in state);
        }

        #endregion

        #region UI 补间 [UI]

        public override long UISliderValue(Slider target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return UISliderValue(target, target.value, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UISliderValue(Slider target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UISliderValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            return TweenTask.Create(in state);
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return UINormalizedPosition(target, target.normalizedPosition, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 startValue, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UINormalizedPosition, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y;
            state.EndX = endValue.x; state.EndY = endValue.y;
            return TweenTask.Create(in state);
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return UIHorizontalNormalizedPosition(target, target.horizontalNormalizedPosition, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIHNormalizedPosition, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            return TweenTask.Create(in state);
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return UIAnchoredPosition(target, target.anchoredPosition, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIAnchoredPosition, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y;
            state.EndX = endValue.x; state.EndY = endValue.y;
            return TweenTask.Create(in state);
        }

        public override long UIAnchoredPositionX(RectTransform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return UIAnchoredPositionX(target, target.anchoredPosition.x, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UIAnchoredPositionX(RectTransform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIAnchoredPositionX, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            return TweenTask.Create(in state);
        }

        public override long UIAnchoredPositionY(RectTransform target, float endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return UIAnchoredPositionY(target, target.anchoredPosition.y, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UIAnchoredPositionY(RectTransform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIAnchoredPositionY, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartY = startValue;
            state.EndY = endValue;
            return TweenTask.Create(in state);
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return UIVerticalNormalizedPosition(target, target.verticalNormalizedPosition, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIVNormalizedPosition, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartY = startValue;
            state.EndY = endValue;
            return TweenTask.Create(in state);
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            return UIAnchoredPosition3D(target, target.anchoredPosition3D, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIAnchoredPosition3D, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            return TweenTask.Create(in state);
        }

        public override long UISizeDelta(RectTransform target, Vector2 endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            return UISizeDelta(target, target.sizeDelta, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UISizeDelta(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UISizeDelta, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y;
            state.EndX = endValue.x; state.EndY = endValue.y;
            return TweenTask.Create(in state);
        }

        public override long Color(Graphic target, Color endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return Color(target, target.color, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Color(Graphic target, Color startValue, Color endValue, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIColor, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartColor = startValue;
            state.EndColor = endValue;
            return TweenTask.Create(in state);
        }

        public override long Alpha(CanvasGroup target, float endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return Alpha(target, target.alpha, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Alpha(CanvasGroup target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UICanvasGroupAlpha, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartExtra = startValue;
            state.EndExtra = endValue;
            return TweenTask.Create(in state);
        }

        public override long Alpha(Graphic target, float endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return Alpha(target, target.color.a, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long Alpha(Graphic target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIGraphicAlpha, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartExtra = startValue;
            state.EndExtra = endValue;
            return TweenTask.Create(in state);
        }

        public override long UIFillAmount(Image target, float endValue, float duration, TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0, bool useUnscaledTime = false,
            Action onComplete = null)
        {
            return UIFillAmount(target, target.fillAmount, endValue, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
        }

        public override long UIFillAmount(Image target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.UIFillAmount, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartExtra = startValue;
            state.EndExtra = endValue;
            return TweenTask.Create(in state);
        }

        #endregion

        #region 贝塞尔路径 [BEZIER PATH]

        public override long MoveBezierPath(Transform target, Vector3[] path, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target, TweenOperationType.MoveBezierPath, duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.PathPoints = path;
            return TweenTask.Create(in state);
        }

        #endregion

        #region 自定义补间 [CUSTOM]

        public override long Custom<T>(T target, Vector3 startValue, Vector3 endValue, float duration, Action<T, Vector3> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            // 捕获引用类型回调，不装箱 T（T 已约束为 class）
            Action<float, float, float> onUpdate = (x, y, z) => onValueChange(target, new Vector3(x, y, z));
            var state = BuildState(target, target as UnityEngine.Object, TweenOperationType.CustomVector3,
                duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            state.OnUpdateXYZ = onUpdate;
            return TweenTask.Create(in state);
        }

        public override long Custom<T>(T target, int startValue, int endValue, float duration, Action<T, int> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            Action<float> onUpdate = v => onValueChange(target, Mathf.RoundToInt(v));
            var state = BuildState(target, target as UnityEngine.Object, TweenOperationType.CustomInt,
                duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            state.OnUpdateFloat = onUpdate;
            return TweenTask.Create(in state);
        }

        public override long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            Action<float> onUpdate = v => onValueChange(target, (long)v);
            var state = BuildState(target, target as UnityEngine.Object, TweenOperationType.CustomLong,
                duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            state.OnUpdateFloat = onUpdate;
            return TweenTask.Create(in state);
        }

        public override long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            Action<float> onUpdate = v => onValueChange(target, v);
            var state = BuildState(target, target as UnityEngine.Object, TweenOperationType.CustomFloat,
                duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            state.OnUpdateFloat = onUpdate;
            return TweenTask.Create(in state);
        }

        #endregion

        #region 自定义补间 — 0GC object 回调 [CUSTOM — 0GC OBJECT]

        /// <summary>
        /// 零分配 Custom：回调直接持有 object 目标，回调与目标分别存入 TweenState，无闭包捕获。
        /// 调用侧使用 static lambda 或方法组时不产生任何堆分配，适合每帧创建的高频补间。
        /// </summary>
        public override long Custom(object target, float startValue, float endValue, float duration, Action<object, float> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target as UnityEngine.Object, TweenOperationType.CustomFloat,
                duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue;
            state.EndX = endValue;
            state.OnUpdateObjectFloat = onValueChange;
            return TweenTask.Create(in state);
        }

        /// <summary>
        /// 零分配 Custom（Vector3）：回调直接持有 object 目标，无闭包捕获。
        /// </summary>
        public override long Custom(object target, Vector3 startValue, Vector3 endValue, float duration, Action<object, Vector3> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = BuildState(target, target as UnityEngine.Object, TweenOperationType.CustomVector3,
                duration, ease, cycles, cycleMode, startDelay, useUnscaledTime, onComplete);
            state.StartX = startValue.x; state.StartY = startValue.y; state.StartZ = startValue.z;
            state.EndX = endValue.x; state.EndY = endValue.y; state.EndZ = endValue.z;
            state.OnUpdateObjectVector3 = onValueChange;
            return TweenTask.Create(in state);
        }

        #endregion
    }
}
