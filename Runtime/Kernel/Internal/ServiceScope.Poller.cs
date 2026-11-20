using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 作用域帧轮询：拓扑序驱动的服务 Tick 派发与异常分级。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="ServiceScope"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal sealed partial class ServiceScope
    {

        // 单一循环体——拦截器已上移至世界层作用域帧边界，本层无快/慢路径分裂。
        // 逐服务耗时统计仅编辑器/开发构建启用（编译期门控，Release 零成本）。

        internal void Tick(float elapseSeconds, float realElapseSeconds)
        {
            SortTickablesIfDirty();
            _isIterating = true;
            // 局部变量阻断编译期可达性折叠——避免 throw 后的熔断补偿代码触发 CS0162（JIT 常量传播，零运行时差异）
            bool rethrow = RETHROW_TICK_EXCEPTIONS;
            try
            {
                int count = _tickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var tickable = _tickables[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    try
                    {
                        tickable.Tick(elapseSeconds, realElapseSeconds);
                        ResetPollFailuresIfAny(tickable, PollCategory.Tick);
                    }
                    catch (Exception ex)
                    {
                        LogTickFailure(tickable, nameof(Tick), ex);
                        bool tripped = RecordPollFailure(tickable, PollCategory.Tick, nameof(Tick));
                        if (rethrow) throw;
                        if (tripped) { i--; count--; }
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    finally { RecordPollDuration(tickable, System.Diagnostics.Stopwatch.GetTimestamp() - start); }
#endif
                }
            }
            finally
            {
                _isIterating = false;
                FlushDisposeIfPending();
                FlushPendingChanges();
            }
        }

        internal void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            SortFixedTickablesIfDirty();
            _isIterating = true;
            bool rethrow = RETHROW_TICK_EXCEPTIONS;
            try
            {
                int count = _fixedTickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var fixedTickable = _fixedTickables[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    try
                    {
                        fixedTickable.FixedTick(elapseSeconds, realElapseSeconds);
                        ResetPollFailuresIfAny(fixedTickable, PollCategory.FixedTick);
                    }
                    catch (Exception ex)
                    {
                        LogTickFailure(fixedTickable, nameof(FixedTick), ex);
                        bool tripped = RecordPollFailure(fixedTickable, PollCategory.FixedTick, nameof(FixedTick));
                        if (rethrow) throw;
                        if (tripped) { i--; count--; }
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    finally { RecordPollDuration(fixedTickable, System.Diagnostics.Stopwatch.GetTimestamp() - start); }
#endif
                }
            }
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
        }

        internal void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            SortLateTickablesIfDirty();
            _isIterating = true;
            bool rethrow = RETHROW_TICK_EXCEPTIONS;
            try
            {
                int count = _lateTickables.Count;
                for (int i = 0; i < count; i++)
                {
                    var lateTickable = _lateTickables[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                    try
                    {
                        lateTickable.LateTick(elapseSeconds, realElapseSeconds);
                        ResetPollFailuresIfAny(lateTickable, PollCategory.LateTick);
                    }
                    catch (Exception ex)
                    {
                        LogTickFailure(lateTickable, nameof(LateTick), ex);
                        bool tripped = RecordPollFailure(lateTickable, PollCategory.LateTick, nameof(LateTick));
                        if (rethrow) throw;
                        if (tripped) { i--; count--; }
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    finally { RecordPollDuration(lateTickable, System.Diagnostics.Stopwatch.GetTimestamp() - start); }
#endif
                }
            }
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
        }

        internal void DrawGizmos()
        {
            SortGizmoDrawablesIfDirty();
            _isIterating = true;
            try
            {
                int count = _gizmoDrawables.Count;
                for (int i = 0; i < count; i++)
                {
                    try { _gizmoDrawables[i].OnDrawGizmos(); }
                    catch (Exception ex)
                    {
                        LogTickFailure(_gizmoDrawables[i], "OnDrawGizmos", ex);
                        if (RETHROW_TICK_EXCEPTIONS) throw;
                    }
                }
            }
            finally { _isIterating = false; FlushDisposeIfPending(); FlushPendingChanges(); }
        }

        private static void LogTickFailure(IService service, string methodName, Exception ex)
        {
            LogUtility.Fatal("Service '{0}' threw in {1}:\n{2}",
                service.GetType().FullName, methodName, ex);
        }

        /// <summary>
        /// 轮询类别。异常熔断按类别独立计数与摘除——某类轮询失败不影响其它类别的连续性判定。
        /// </summary>
        private enum PollCategory : byte
        {
            Tick = 0,
            FixedTick = 1,
            LateTick = 2,
        }

        /// <summary>
        /// 记录一次轮询异常并按需熔断。
        /// </summary>
        /// <param name="service">抛出异常的服务实例。</param>
        /// <param name="category">轮询类别（独立计数）。</param>
        /// <param name="methodName">轮询方法名（告警文案用）。</param>
        /// <returns>是否已将服务从对应轮询列表移除；迭代方需回退索引以补偿 swap-remove 移位。</returns>
        private bool RecordPollFailure(IService service, PollCategory category, string methodName)
        {
            _hasPollFailures = true;

            if (!_entriesByService.TryGetValue(service, out var entry))
                return false;

            int failures;
            switch (category)
            {
                case PollCategory.Tick:
                    failures = ++entry.TickConsecutiveFailures;
                    break;
                case PollCategory.FixedTick:
                    failures = ++entry.FixedTickConsecutiveFailures;
                    break;
                default:
                    failures = ++entry.LateTickConsecutiveFailures;
                    break;
            }

            _entriesByService[service] = entry;

            if (failures < _world.TickFailureTripThreshold) return false;

            TripFromPollList(service, entry, category, methodName, failures);
            return true;
        }

        /// <summary>
        /// 熔断：将服务从对应轮询类别移除（swap-remove O(1)）并汇总告警一次。
        /// </summary>
        /// <remarks>
        /// 服务条目保留——仍可解析、仍参与其它类别轮询；重新注册即完全重置。
        /// </remarks>
        private void TripFromPollList(IService service, ServiceEntry entry, PollCategory category, string methodName, int failures)
        {
            switch (category)
            {
                case PollCategory.Tick:
                    if (entry.TickIndex != MISSING_INDEX) RemoveTickableAt(entry.TickIndex);
                    entry.TickIndex = MISSING_INDEX;
                    break;
                case PollCategory.FixedTick:
                    if (entry.FixedTickIndex != MISSING_INDEX) RemoveFixedTickableAt(entry.FixedTickIndex);
                    entry.FixedTickIndex = MISSING_INDEX;
                    break;
                default:
                    if (entry.LateTickIndex != MISSING_INDEX) RemoveLateTickableAt(entry.LateTickIndex);
                    entry.LateTickIndex = MISSING_INDEX;
                    break;
            }

            _entriesByService[service] = entry;

            LogUtility.Warning(
                "Service '{0}' was removed from {1} polling after {2} consecutive failures (trip threshold {3}).",
                service.GetType().FullName, methodName, failures, _world.TickFailureTripThreshold);
        }

        /// <summary>
        /// 对应类别成功一次即清零该类别的连续失败计数。
        /// </summary>
        /// <remarks>
        /// 仅在发生过失败后才有实际开销（<see cref="_hasPollFailures"/> 常态为 false，健康服务热路径零字典访问）。
        /// </remarks>
        private void ResetPollFailuresIfAny(IService service, PollCategory category)
        {
            if (!_hasPollFailures) return;

            if (!_entriesByService.TryGetValue(service, out var entry))
                return;

            switch (category)
            {
                case PollCategory.Tick:
                    if (entry.TickConsecutiveFailures == 0) return;
                    entry.TickConsecutiveFailures = 0;
                    break;
                case PollCategory.FixedTick:
                    if (entry.FixedTickConsecutiveFailures == 0) return;
                    entry.FixedTickConsecutiveFailures = 0;
                    break;
                default:
                    if (entry.LateTickConsecutiveFailures == 0) return;
                    entry.LateTickConsecutiveFailures = 0;
                    break;
            }

            _entriesByService[service] = entry;
        }

        /// <summary>
        /// 记录单次轮询耗时。仅编辑器/开发构建写入；Release 下方法体为空，JIT 裁剪为零开销。
        /// </summary>
        private void RecordPollDuration(IService service, long elapsedTimestamps)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!_entriesByService.TryGetValue(service, out var entry))
                return;

            float ms = (float)(elapsedTimestamps * TIMESTAMP_TO_MS);
            entry.PollSamples++;
            entry.PollTotalMs += ms;
            if (ms > entry.PollPeakMs) entry.PollPeakMs = ms;

            _entriesByService[service] = entry;
#endif
        }
    }
}
