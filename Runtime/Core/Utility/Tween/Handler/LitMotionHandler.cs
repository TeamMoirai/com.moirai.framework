#if LITMOTION_INSTALLED
using System;
using System.Collections.Generic;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos
{
    /// <summary>
    /// LitMotion 动画处理器实现（零GC）。
    /// 使用 Dictionary 管理所有活跃的 MotionHandle。
    /// 每个方法内联构建器链，使用 state-based Bind 避免闭包分配。
    /// </summary>
    [Serializable]
    public sealed class LitMotionHandler : TweenHandler
    {
        #region 字段

        private readonly Dictionary<long, MotionHandle> _handleMap = new Dictionary<long, MotionHandle>();
        private static readonly List<long> s_TempList = new List<long>();

        #endregion

        #region 基础方法

        protected override void OnInit() { }

        protected override void Shutdown()
        {
            _handleMap.Clear();
        }

        public override void ReleaseUnusedTween()
        {
            s_TempList.Clear();
            foreach (var kvp in _handleMap)
            {
                if (!kvp.Value.IsActive())
                    s_TempList.Add(kvp.Key);
            }

            for (int i = 0; i < s_TempList.Count; i++)
                _handleMap.Remove(s_TempList[i]);

            s_TempList.Clear();
        }

        public override bool IsTweening(object onTarget)
        {
            if (onTarget == null) return false;
            foreach (var kvp in _handleMap)
            {
                if (kvp.Value.IsActive()) return true;
            }
            return false;
        }

        public override int GetTweenCount(object onTarget)
        {
            if (onTarget == null) return 0;
            int count = 0;
            foreach (var kvp in _handleMap)
            {
                if (kvp.Value.IsActive()) count++;
            }
            return count;
        }

        public override bool IsAlive(long tweenId)
        {
            return _handleMap.TryGetValue(tweenId, out var handle) && handle.IsActive();
        }

        public override void Stop(long tweenId)
        {
            if (_handleMap.TryGetValue(tweenId, out var handle))
            {
                handle.TryCancel();
                _handleMap.Remove(tweenId);
            }
        }

        public override void Complete(long tweenId)
        {
            if (_handleMap.TryGetValue(tweenId, out var handle))
            {
                handle.TryComplete();
                _handleMap.Remove(tweenId);
            }
        }

        public override int StopAll(object onTarget = null)
        {
            int stopped = 0;
            foreach (var kvp in _handleMap)
            {
                if (kvp.Value.IsActive())
                {
                    kvp.Value.TryCancel();
                    stopped++;
                }
            }
            _handleMap.Clear();
            return stopped;
        }

        public override int CompleteAll(object onTarget = null)
        {
            int completed = 0;
            foreach (var kvp in _handleMap)
            {
                if (kvp.Value.IsActive())
                {
                    kvp.Value.TryComplete();
                    completed++;
                }
            }
            _handleMap.Clear();
            return completed;
        }
        
        #endregion

        #region 辅助方法

        private long RegisterHandle(MotionHandle handle)
        {
            var id = handle.StorageId;
            _handleMap[id] = handle;
            return id;
        }

        private static IMotionScheduler GetScheduler(bool useUnscaledTime)
        {
            return useUnscaledTime ? MotionScheduler.UpdateIgnoreTimeScale : MotionScheduler.Update;
        }

        #endregion

        #region Delay

        public override long Delay(float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            var builder = LMotion.Create(0f, 1f, duration)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.RunWithoutBinding());
        }

        public override long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            var builder = LMotion.Create(0f, 1f, duration)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.RunWithoutBinding());
        }

        #endregion

        #region Transform 补间 — LocalRotation (Vector3)

        public override long LocalRotation(Transform target, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localEulerAngles, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalEulerAngles(target));
        }

        public override long LocalRotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalEulerAngles(target));
        }

        #endregion

        #region Transform 补间 — Scale (float)

        public override long Scale(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.x, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.localScale = new Vector3(v, v, v)));
        }

        public override long Scale(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.localScale = new Vector3(v, v, v)));
        }

        #endregion

        #region Transform 补间 — Rotation (Vector3)

        public override long Rotation(Transform target, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.eulerAngles, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToEulerAngles(target));
        }

        public override long Rotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToEulerAngles(target));
        }

        #endregion

        #region Transform 补间 — Position

        public override long Position(Transform target, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPosition(target));
        }

        public override long Position(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPosition(target));
        }

        #endregion

        #region Transform 补间 — PositionX / Y / Z

        public override long PositionX(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position.x, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPositionX(target));
        }

        public override long PositionX(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPositionX(target));
        }

        public override long PositionY(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position.y, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPositionY(target));
        }

        public override long PositionY(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPositionY(target));
        }

        public override long PositionZ(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position.z, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPositionZ(target));
        }

        public override long PositionZ(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToPositionZ(target));
        }

        #endregion

        #region Transform 补间 — LocalPosition

        public override long LocalPosition(Transform target, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPosition(target));
        }

        public override long LocalPosition(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPosition(target));
        }

        #endregion

        #region Transform 补间 — LocalPositionX / Y / Z

        public override long LocalPositionX(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition.x, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPositionX(target));
        }

        public override long LocalPositionX(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPositionX(target));
        }

        public override long LocalPositionY(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition.y, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPositionY(target));
        }

        public override long LocalPositionY(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPositionY(target));
        }

        public override long LocalPositionZ(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition.z, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPositionZ(target));
        }

        public override long LocalPositionZ(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalPositionZ(target));
        }

        #endregion

        #region Transform 补间 — Rotation (Quaternion)

        public override long Rotation(Transform target, Quaternion endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.rotation, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToRotation(target));
        }

        public override long Rotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToRotation(target));
        }

        public override long LocalRotation(Transform target, Quaternion endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localRotation, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalRotation(target));
        }

        public override long LocalRotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalRotation(target));
        }

        #endregion

        #region Transform 补间 — Scale (Vector3)

        public override long Scale(Transform target, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScale(target));
        }

        public override long Scale(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScale(target));
        }

        #endregion

        #region Transform 补间 — ScaleX / Y / Z

        public override long ScaleX(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.x, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScaleX(target));
        }

        public override long ScaleX(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScaleX(target));
        }

        public override long ScaleY(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.y, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScaleY(target));
        }

        public override long ScaleY(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScaleY(target));
        }

        public override long ScaleZ(Transform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.z, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScaleZ(target));
        }

        public override long ScaleZ(Transform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToLocalScaleZ(target));
        }

        #endregion

        #region SpriteRenderer / Material 补间

        public override long Color(SpriteRenderer target, Color endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColor(target));
        }

        public override long Color(SpriteRenderer target, Color startValue, Color endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColor(target));
        }

        public override long MaterialColor(Material target, Color startValue, Color endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToMaterialColor(target, 0));
        }

        public override long Alpha(SpriteRenderer target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color.a, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColorA(target));
        }

        public override long Alpha(SpriteRenderer target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColorA(target));
        }

        #endregion

        #region UI 补间

        public override long UISliderValue(Slider target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.value, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.value = v));
        }

        public override long UISliderValue(Slider target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.value = v));
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.normalizedPosition, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.normalizedPosition = v));
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 startValue, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.normalizedPosition = v));
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.horizontalNormalizedPosition, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.horizontalNormalizedPosition = v));
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.horizontalNormalizedPosition = v));
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.verticalNormalizedPosition, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.verticalNormalizedPosition = v));
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.verticalNormalizedPosition = v));
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPosition(target));
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPosition(target));
        }

        public override long UIAnchoredPositionX(RectTransform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition.x, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPositionX(target));
        }

        public override long UIAnchoredPositionX(RectTransform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPositionX(target));
        }

        public override long UIAnchoredPositionY(RectTransform target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition.y, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPositionY(target));
        }

        public override long UIAnchoredPositionY(RectTransform target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPositionY(target));
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition3D, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPosition3D(target));
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToAnchoredPosition3D(target));
        }

        public override long UISizeDelta(RectTransform target, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.sizeDelta, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToSizeDelta(target));
        }

        public override long UISizeDelta(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToSizeDelta(target));
        }

        public override long Color(Graphic target, Color endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColor(target));
        }

        public override long Color(Graphic target, Color startValue, Color endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColor(target));
        }

        public override long Alpha(CanvasGroup target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.alpha, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.alpha = v));
        }

        public override long Alpha(CanvasGroup target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => t.alpha = v));
        }

        public override long Alpha(Graphic target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color.a, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColorA(target));
        }

        public override long Alpha(Graphic target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToColorA(target));
        }

        public override long UIFillAmount(Image target, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.fillAmount, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToFillAmount(target));
        }

        public override long UIFillAmount(Image target, float startValue, float endValue, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.BindToFillAmount(target));
        }

        #endregion

        #region Bezier Path

        public override long MoveBezierPath(Transform target, Vector3[] path, float duration,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (path == null || path.Length < 2) throw new ArgumentException("Path must have at least 2 points.");

            var builder = LMotion.Create(0f, 1f, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (t, transform) =>
            {
                transform.position = CalculateBezierPoint(t, path);

                if (Mathf.Approximately(t, 1f))
                {
                    transform.position = path[^1];
                }
            }));
        }

        #endregion

        #region Custom

        public override long Custom<T>(T target, Vector3 startValue, Vector3 endValue, float duration,
            Action<T, Vector3> onValueChange, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null) where T : class
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => onValueChange(t, v)));
        }

        public override long Custom<T>(T target, int startValue, int endValue, float duration, Action<T, int> onValueChange,
            TweenUtility.EEase ease = TweenUtility.EEase.Default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                    .WithEase(ease.ToLitMotionEase())
                    .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                    .WithDelay(startDelay)
                    .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => onValueChange(t, v)));
        }

        public override long Custom<T>(T target, long startValue, long endValue, float duration,
            Action<T, long> onValueChange, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null) where T : class
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => onValueChange(t, v)));
        }

        public override long Custom<T>(T target, float startValue, float endValue, float duration,
            Action<T, float> onValueChange, TweenUtility.EEase ease = TweenUtility.EEase.Default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null) where T : class
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithEase(ease.ToLitMotionEase())
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime));
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => onValueChange(t, v)));
        }

        #endregion
    }
}
#endif
