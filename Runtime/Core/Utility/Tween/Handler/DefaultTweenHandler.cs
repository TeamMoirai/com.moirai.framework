using System;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认补间动画处理器。基于结构体数组 + 版本号ID实现，0 GC、高性能。
    /// </summary>
    [Serializable]
    public sealed partial class DefaultTweenHandler : TweenHandler
    {
        #region 生命周期

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

        #endregion

        #region Delay

        public override long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false,
            bool warnIfTargetDestroyed = true)
        {
            return CreateDelay(null, duration, onComplete, useUnscaledTime);
        }

        public override long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false,
            bool warnIfTargetDestroyed = true)
        {
            return CreateDelay(target, duration, onComplete, useUnscaledTime);
        }

        private long CreateDelay(object target, float duration, Action onComplete, bool useUnscaledTime)
        {
            var state = new TweenState
            {
                Target = target,
                UnityObject = target as UnityEngine.Object,
                Duration = duration,
                OperationType = TweenOperationType.Delay,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                Cycles = 1,
                CurrentCycle = 0,
                CycleMode = TweenUtility.ECycleMode.Restart,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Position

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z,
                OperationType = TweenOperationType.Position,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — PositionX / Y / Z

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.PositionX,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartY = startValue,
                EndY = endValue,
                OperationType = TweenOperationType.PositionY,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartZ = startValue,
                EndZ = endValue,
                OperationType = TweenOperationType.PositionZ,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalPosition

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z,
                OperationType = TweenOperationType.LocalPosition,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalPositionX / Y / Z

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.LocalPositionX,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartY = startValue,
                EndY = endValue,
                OperationType = TweenOperationType.LocalPositionY,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartZ = startValue,
                EndZ = endValue,
                OperationType = TweenOperationType.LocalPositionZ,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Rotation (Vector3)

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z,
                OperationType = TweenOperationType.RotationVec3,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalRotation (Vector3)

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z,
                OperationType = TweenOperationType.LocalRotationVec3,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Rotation (Quaternion)

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z, StartExtra = startValue.w,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z, EndExtra = endValue.w,
                OperationType = TweenOperationType.RotationQuat,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — LocalRotation (Quaternion)

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z, StartExtra = startValue.w,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z, EndExtra = endValue.w,
                OperationType = TweenOperationType.LocalRotationQuat,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Scale (float)

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.ScaleFloat,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — Scale (Vector3)

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z,
                OperationType = TweenOperationType.ScaleVec3,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Transform 补间 — ScaleX / Y / Z

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.ScaleX,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartY = startValue,
                EndY = endValue,
                OperationType = TweenOperationType.ScaleY,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartZ = startValue,
                EndZ = endValue,
                OperationType = TweenOperationType.ScaleZ,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region SpriteRenderer / Material 补间

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartColor = startValue,
                EndColor = endValue,
                OperationType = TweenOperationType.SpriteColor,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartExtra = startValue,
                EndExtra = endValue,
                OperationType = TweenOperationType.SpriteAlpha,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        public override long MaterialColor(Material target, Color startValue, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartColor = startValue,
                EndColor = endValue,
                OperationType = TweenOperationType.MaterialColor,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region UI 补间

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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.UISliderValue,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y,
                EndX = endValue.x, EndY = endValue.y,
                OperationType = TweenOperationType.UINormalizedPosition,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.UIHNormalizedPosition,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y,
                EndX = endValue.x, EndY = endValue.y,
                OperationType = TweenOperationType.UIAnchoredPosition,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.UIAnchoredPositionX,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartY = startValue,
                EndY = endValue,
                OperationType = TweenOperationType.UIAnchoredPositionY,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartY = startValue,
                EndY = endValue,
                OperationType = TweenOperationType.UIVNormalizedPosition,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z,
                OperationType = TweenOperationType.UIAnchoredPosition3D,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y,
                EndX = endValue.x, EndY = endValue.y,
                OperationType = TweenOperationType.UISizeDelta,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartColor = startValue,
                EndColor = endValue,
                OperationType = TweenOperationType.UIColor,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartExtra = startValue,
                EndExtra = endValue,
                OperationType = TweenOperationType.UICanvasGroupAlpha,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartExtra = startValue,
                EndExtra = endValue,
                OperationType = TweenOperationType.UIGraphicAlpha,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
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
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                StartExtra = startValue,
                EndExtra = endValue,
                OperationType = TweenOperationType.UIFillAmount,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Bezier Path

        public override long MoveBezierPath(Transform target, Vector3[] path, float duration, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart, float startDelay = 0,
            bool useUnscaledTime = false, Action onComplete = null)
        {
            var state = new TweenState
            {
                Target = target,
                UnityObject = target,
                Duration = duration,
                PathPoints = path,
                OperationType = TweenOperationType.MoveBezierPath,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion

        #region Custom

        public override long Custom<T>(T target, Vector3 startValue, Vector3 endValue, float duration, Action<T, Vector3> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            // 捕获引用类型回调，不装箱 T（T 已约束为 class）
            Action<float, float, float> onUpdate = (x, y, z) => onValueChange(target, new Vector3(x, y, z));
            var state = new TweenState
            {
                Target = target,
                UnityObject = target as UnityEngine.Object,
                Duration = duration,
                StartX = startValue.x, StartY = startValue.y, StartZ = startValue.z,
                EndX = endValue.x, EndY = endValue.y, EndZ = endValue.z,
                OperationType = TweenOperationType.CustomVector3,
                OnUpdateXYZ = onUpdate,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        public override long Custom<T>(T target, int startValue, int endValue, float duration, Action<T, int> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            Action<float> onUpdate = (v) => onValueChange(target, Mathf.RoundToInt(v));
            var state = new TweenState
            {
                Target = target,
                UnityObject = target as UnityEngine.Object,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.CustomInt,
                OnUpdateFloat = onUpdate,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        public override long Custom<T>(T target, long startValue, long endValue, float duration, Action<T, long> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            Action<float> onUpdate = (v) => onValueChange(target, (long)v);
            var state = new TweenState
            {
                Target = target,
                UnityObject = target as UnityEngine.Object,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.CustomLong,
                OnUpdateFloat = onUpdate,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        public override long Custom<T>(T target, float startValue, float endValue, float duration, Action<T, float> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            Action<float> onUpdate = (v) => onValueChange(target, v);
            var state = new TweenState
            {
                Target = target,
                UnityObject = target as UnityEngine.Object,
                Duration = duration,
                StartX = startValue,
                EndX = endValue,
                OperationType = TweenOperationType.CustomFloat,
                OnUpdateFloat = onUpdate,
                OnComplete = onComplete,
                UseUnscaledTime = useUnscaledTime,
                HasDelay = startDelay > 0f,
                StartDelay = startDelay,
                Ease = ease,
                Cycles = cycles,
                CurrentCycle = 0,
                CycleMode = cycleMode,
            };
            return TweenTask.Create(in state);
        }

        #endregion
    }
}
