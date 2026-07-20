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
        private readonly Dictionary<long, object> _targetMap = new Dictionary<long, object>();
        private static readonly List<long> s_TempList = new List<long>();

        #endregion

        #region 基础方法

        protected override void OnInit() { }

        protected override void Shutdown()
        {
            _handleMap.Clear();
            _targetMap.Clear();
        }

        public override void ReleaseUnusedTween()
        {
            s_TempList.Clear();
            foreach (var kvp in _handleMap)
            {
                var inactive = !kvp.Value.IsActive();
                var destroyed = _targetMap.TryGetValue(kvp.Key, out var t)
                    && t is UnityEngine.Object uo && uo == null;
                if (inactive || destroyed)
                    s_TempList.Add(kvp.Key);
            }
            for (int i = 0; i < s_TempList.Count; i++)
            {
                var id = s_TempList[i];
                if (_handleMap.TryGetValue(id, out var handle))
                    handle.TryCancel();
                _handleMap.Remove(id);
                _targetMap.Remove(id);
            }
            s_TempList.Clear();
        }

        public override bool IsTweening(object onTarget)
        {
            if (onTarget == null)
            {
                foreach (var kvp in _handleMap)
                    if (kvp.Value.IsActive()) return true;
                return false;
            }

            foreach (var kvp in _handleMap)
            {
                if (kvp.Value.IsActive()
                    && _targetMap.TryGetValue(kvp.Key, out var t) && ReferenceEquals(t, onTarget))
                    return true;
            }
            return false;
        }

        public override int GetTweenCount(object onTarget)
        {
            if (onTarget == null)
            {
                int count = 0;
                foreach (var kvp in _handleMap)
                    if (kvp.Value.IsActive()) count++;
                return count;
            }

            int filtered = 0;
            foreach (var kvp in _handleMap)
            {
                if (kvp.Value.IsActive()
                    && _targetMap.TryGetValue(kvp.Key, out var t) && ReferenceEquals(t, onTarget))
                    filtered++;
            }
            return filtered;
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
                _targetMap.Remove(tweenId);
            }
        }

        public override void Complete(long tweenId)
        {
            if (_handleMap.TryGetValue(tweenId, out var handle))
            {
                handle.TryComplete();
                _handleMap.Remove(tweenId);
                _targetMap.Remove(tweenId);
            }
        }

        public override int StopAll(object onTarget = null)
        {
            int stopped = 0;

            if (onTarget == null)
            {
                // 无目标 → 全部停止
                foreach (var kvp in _handleMap)
                {
                    if (kvp.Value.IsActive())
                    {
                        kvp.Value.TryCancel();
                        stopped++;
                    }
                }
                _handleMap.Clear();
                _targetMap.Clear();
            }
            else
            {
                // 有目标 → 仅停止匹配项
                s_TempList.Clear();
                foreach (var kvp in _handleMap)
                {
                    if (kvp.Value.IsActive()
                        && _targetMap.TryGetValue(kvp.Key, out var t) && ReferenceEquals(t, onTarget))
                    {
                        kvp.Value.TryCancel();
                        stopped++;
                        s_TempList.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < s_TempList.Count; i++)
                {
                    _handleMap.Remove(s_TempList[i]);
                    _targetMap.Remove(s_TempList[i]);
                }
                s_TempList.Clear();
            }

            return stopped;
        }

        public override int CompleteAll(object onTarget = null)
        {
            int completed = 0;

            if (onTarget == null)
            {
                foreach (var kvp in _handleMap)
                {
                    if (kvp.Value.IsActive())
                    {
                        kvp.Value.TryComplete();
                        completed++;
                    }
                }
                _handleMap.Clear();
                _targetMap.Clear();
            }
            else
            {
                s_TempList.Clear();
                foreach (var kvp in _handleMap)
                {
                    if (kvp.Value.IsActive()
                        && _targetMap.TryGetValue(kvp.Key, out var t) && ReferenceEquals(t, onTarget))
                    {
                        kvp.Value.TryComplete();
                        completed++;
                        s_TempList.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < s_TempList.Count; i++)
                {
                    _handleMap.Remove(s_TempList[i]);
                    _targetMap.Remove(s_TempList[i]);
                }
                s_TempList.Clear();
            }

            return completed;
        }
        
        #endregion

        #region 辅助方法

        private long RegisterHandle(MotionHandle handle, object target = null)
        {
            var id = handle.StorageId;
            _handleMap[id] = handle;
            if (target != null)
                _targetMap[id] = target;
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
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.RunWithoutBinding());
        }

        public override long Delay(object target, float duration, Action onComplete = null, bool useUnscaledTime = false, bool warnIfTargetDestroyed = true)
        {
            var builder = LMotion.Create(0f, 1f, duration)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();
            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.RunWithoutBinding(), target);
        }

        #endregion

        #region Transform 补间 — LocalRotation (Vector3)

        public override long LocalRotation(Transform target, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localEulerAngles, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.localEulerAngles = x; }), target);
        }

        public override long LocalRotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.localEulerAngles = x; }), target);
        }

        #endregion

        #region Transform 补间 — Scale (float)

        public override long Scale(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.x, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, Transform t) => { if (t != null) t.localScale = new Vector3(v, v, v); }), target);
        }

        public override long Scale(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, Transform t) => { if (t != null) t.localScale = new Vector3(v, v, v); }), target);
        }

        #endregion

        #region Transform 补间 — Rotation (Vector3)

        public override long Rotation(Transform target, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.eulerAngles, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.eulerAngles = x; }), target);
        }

        public override long Rotation(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.eulerAngles = x; }), target);
        }

        #endregion

        #region Transform 补间 — Position

        public override long Position(Transform target, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.position = x; }), target);
        }

        public override long Position(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.position = x; }), target);
        }

        #endregion

        #region Transform 补间 — PositionX / Y / Z

        public override long PositionX(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position.x, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.position; p.x = x; t.position = p; } }), target);
        }

        public override long PositionX(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.position; p.x = x; t.position = p; } }), target);
        }

        public override long PositionY(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position.y, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.position; p.y = x; t.position = p; } }), target);
        }

        public override long PositionY(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.position; p.y = x; t.position = p; } }), target);
        }

        public override long PositionZ(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.position.z, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.position; p.z = x; t.position = p; } }), target);
        }

        public override long PositionZ(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.position; p.z = x; t.position = p; } }), target);
        }

        #endregion

        #region Transform 补间 — LocalPosition

        public override long LocalPosition(Transform target, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.localPosition = x; }), target);
        }

        public override long LocalPosition(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.localPosition = x; }), target);
        }

        #endregion

        #region Transform 补间 — LocalPositionX / Y / Z

        public override long LocalPositionX(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition.x, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localPosition; p.x = x; t.localPosition = p; } }), target);
        }

        public override long LocalPositionX(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localPosition; p.x = x; t.localPosition = p; } }), target);
        }

        public override long LocalPositionY(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition.y, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localPosition; p.y = x; t.localPosition = p; } }), target);
        }

        public override long LocalPositionY(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localPosition; p.y = x; t.localPosition = p; } }), target);
        }

        public override long LocalPositionZ(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localPosition.z, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localPosition; p.z = x; t.localPosition = p; } }), target);
        }

        public override long LocalPositionZ(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localPosition; p.z = x; t.localPosition = p; } }), target);
        }

        #endregion

        #region Transform 补间 — Rotation (Quaternion)

        public override long Rotation(Transform target, Quaternion endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.rotation, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Quaternion x, Transform t) => { if (t != null) t.rotation = x; }), target);
        }

        public override long Rotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Quaternion x, Transform t) => { if (t != null) t.rotation = x; }), target);
        }

        public override long LocalRotation(Transform target, Quaternion endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localRotation, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Quaternion x, Transform t) => { if (t != null) t.localRotation = x; }), target);
        }

        public override long LocalRotation(Transform target, Quaternion startValue, Quaternion endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Quaternion x, Transform t) => { if (t != null) t.localRotation = x; }), target);
        }

        #endregion

        #region Transform 补间 — Scale (Vector3)

        public override long Scale(Transform target, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.localScale = x; }), target);
        }

        public override long Scale(Transform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, Transform t) => { if (t != null) t.localScale = x; }), target);
        }

        #endregion

        #region Transform 补间 — ScaleX / Y / Z

        public override long ScaleX(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.x, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localScale; p.x = x; t.localScale = p; } }), target);
        }

        public override long ScaleX(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localScale; p.x = x; t.localScale = p; } }), target);
        }

        public override long ScaleY(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.y, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localScale; p.y = x; t.localScale = p; } }), target);
        }

        public override long ScaleY(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localScale; p.y = x; t.localScale = p; } }), target);
        }

        public override long ScaleZ(Transform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.localScale.z, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localScale; p.z = x; t.localScale = p; } }), target);
        }

        public override long ScaleZ(Transform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Transform t) => { if (t != null) { var p = t.localScale; p.z = x; t.localScale = p; } }), target);
        }

        #endregion

        #region SpriteRenderer / Material 补间

        public override long Color(SpriteRenderer target, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Color x, SpriteRenderer t) => { if (t != null) t.color = x; }), target);
        }

        public override long Color(SpriteRenderer target, Color startValue, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Color x, SpriteRenderer t) => { if (t != null) t.color = x; }), target);
        }

        public override long MaterialColor(Material target, Color startValue, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Color x, Material m) => { if (m != null) m.color = x; }));
        }

        public override long Alpha(SpriteRenderer target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color.a, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, SpriteRenderer t) => { if (t != null) { var c = t.color; c.a = x; t.color = c; } }), target);
        }

        public override long Alpha(SpriteRenderer target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, SpriteRenderer t) => { if (t != null) { var c = t.color; c.a = x; t.color = c; } }), target);
        }

        #endregion

        #region UI 补间

        public override long UISliderValue(Slider target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.value, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, Slider t) => { if (t != null) t.value = v; }));
        }

        public override long UISliderValue(Slider target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, Slider t) => { if (t != null) t.value = v; }));
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.normalizedPosition, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector2 v, ScrollRect t) => { if (t != null) t.normalizedPosition = v; }));
        }

        public override long UINormalizedPosition(ScrollRect target, Vector2 startValue, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector2 v, ScrollRect t) => { if (t != null) t.normalizedPosition = v; }));
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.horizontalNormalizedPosition, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, ScrollRect t) => { if (t != null) t.horizontalNormalizedPosition = v; }));
        }

        public override long UIHorizontalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, ScrollRect t) => { if (t != null) t.horizontalNormalizedPosition = v; }));
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.verticalNormalizedPosition, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, ScrollRect t) => { if (t != null) t.verticalNormalizedPosition = v; }));
        }

        public override long UIVerticalNormalizedPosition(ScrollRect target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, ScrollRect t) => { if (t != null) t.verticalNormalizedPosition = v; }));
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector2 x, RectTransform t) => { if (t != null) t.anchoredPosition = x; }), target);
        }

        public override long UIAnchoredPosition(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector2 x, RectTransform t) => { if (t != null) t.anchoredPosition = x; }), target);
        }

        public override long UIAnchoredPositionX(RectTransform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition.x, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, RectTransform t) => { if (t != null) { var p = t.anchoredPosition; p.x = x; t.anchoredPosition = p; } }), target);
        }

        public override long UIAnchoredPositionX(RectTransform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, RectTransform t) => { if (t != null) { var p = t.anchoredPosition; p.x = x; t.anchoredPosition = p; } }), target);
        }

        public override long UIAnchoredPositionY(RectTransform target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition.y, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, RectTransform t) => { if (t != null) { var p = t.anchoredPosition; p.y = x; t.anchoredPosition = p; } }), target);
        }

        public override long UIAnchoredPositionY(RectTransform target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, RectTransform t) => { if (t != null) { var p = t.anchoredPosition; p.y = x; t.anchoredPosition = p; } }), target);
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.anchoredPosition3D, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, RectTransform t) => { if (t != null) t.anchoredPosition3D = x; }), target);
        }

        public override long UIAnchoredPosition3D(RectTransform target, Vector3 startValue, Vector3 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector3 x, RectTransform t) => { if (t != null) t.anchoredPosition3D = x; }), target);
        }

        public override long UISizeDelta(RectTransform target, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.sizeDelta, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector2 x, RectTransform t) => { if (t != null) t.sizeDelta = x; }), target);
        }

        public override long UISizeDelta(RectTransform target, Vector2 startValue, Vector2 endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Vector2 x, RectTransform t) => { if (t != null) t.sizeDelta = x; }), target);
        }

        public override long Color(Graphic target, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Color x, Graphic t) => { if (t != null) t.color = x; }), target);
        }

        public override long Color(Graphic target, Color startValue, Color endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (Color x, Graphic t) => { if (t != null) t.color = x; }), target);
        }

        public override long Alpha(CanvasGroup target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.alpha, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, CanvasGroup t) => { if (t != null) t.alpha = v; }));
        }

        public override long Alpha(CanvasGroup target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float v, CanvasGroup t) => { if (t != null) t.alpha = v; }));
        }

        public override long Alpha(Graphic target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.color.a, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Graphic t) => { if (t != null) { var c = t.color; c.a = x; t.color = c; } }), target);
        }

        public override long Alpha(Graphic target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Graphic t) => { if (t != null) { var c = t.color; c.a = x; t.color = c; } }), target);
        }

        public override long UIFillAmount(Image target, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(target.fillAmount, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Image t) => { if (t != null) t.fillAmount = x; }), target);
        }

        public override long UIFillAmount(Image target, float startValue, float endValue, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, static (float x, Image t) => { if (t != null) t.fillAmount = x; }), target);
        }

        #endregion

        #region Bezier Path

        public override long MoveBezierPath(Transform target, Vector3[] path, float duration,
            TweenEase ease = default, int cycles = 1,
            TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (path == null || path.Length < 2) throw new ArgumentException("Path must have at least 2 points.");

            var builder = LMotion.Create(0f, 1f, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (float t, Transform transform) =>
            {
                if (transform == null) return;
                transform.position = CalculateBezierPoint(t, path);

                if (Mathf.Approximately(t, 1f))
                {
                    transform.position = path[^1];
                }
            }), target);
        }

        #endregion

        #region Custom

        public override long Custom<T>(T target, Vector3 startValue, Vector3 endValue, float duration,
            Action<T, Vector3> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null) where T : class
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => { if (t != null) onValueChange(t, v); }), target);
        }

        public override long Custom<T>(T target, int startValue, int endValue, float duration, Action<T, int> onValueChange,
            TweenEase ease = default, int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null)
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => { if (t != null) onValueChange(t, v); }), target);
        }

        public override long Custom<T>(T target, long startValue, long endValue, float duration,
            Action<T, long> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null) where T : class
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => { if (t != null) onValueChange(t, v); }), target);
        }

        public override long Custom<T>(T target, float startValue, float endValue, float duration,
            Action<T, float> onValueChange, TweenEase ease = default,
            int cycles = 1, TweenUtility.ECycleMode cycleMode = TweenUtility.ECycleMode.Restart,
            float startDelay = 0, bool useUnscaledTime = false, Action onComplete = null) where T : class
        {
            var builder = LMotion.Create(startValue, endValue, duration)
                .WithLoops(cycles, cycleMode.ToLitMotionLoopType())
                .WithDelay(startDelay)
                .WithScheduler(GetScheduler(useUnscaledTime))
                .WithCancelOnError();

            if (ease.IsCurve) builder.WithEase(ease.AnimationCurve);
            else builder.WithEase(ease.EaseType.ToLitMotionEase());

            if (onComplete != null) builder = builder.WithOnComplete(onComplete);
            return RegisterHandle(builder.Bind(target, (v, t) => { if (t != null) onValueChange(t, v); }), target);
        }

        #endregion
    }
}
#endif
