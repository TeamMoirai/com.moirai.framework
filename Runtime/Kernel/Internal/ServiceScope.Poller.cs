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
                        LogTickFailureFirst(tickable, PollCategory.Tick, nameof(Tick), ex);
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
                        LogTickFailureFirst(fixedTickable, PollCategory.FixedTick, nameof(FixedTick), ex);
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
                        LogTickFailureFirst(lateTickable, PollCategory.LateTick, nameof(LateTick), ex);
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
        /// 轮询异常限流记录：该服务在对应类别的<b>首错</b>才 Fatal 带栈完整记录，后续失败静默计数——
        /// 连续故障逐帧 Fatal 会把日志通道刷满，掩盖其它系统的故障信号；熔断摘除时的汇总告警（Warning）仍会发出一次。
        /// </summary>
        /// <remarks>
        /// 判定与递增次序必须严格：本方法先查首错标志并置位，再由 <see cref="RecordPollFailure"/> 递增计数—— <br />
        /// 标志独立于计数（值类型条目以「先查后写」单点收口，避免计数残值与判定窗口的组合漂移）； <br />
        /// 计数被 <see cref="ResetPollFailuresIfAny"/> 清零（成功恢复）时标志一并复位，下一轮故障重新武装。
        /// </remarks>
        private void LogTickFailureFirst(IService service, PollCategory category, string methodName, Exception ex)
        {
            if (_entriesByService.TryGetValue(service, out var entry))
            {
                bool alreadyLogged = category switch
                {
                    PollCategory.Tick => entry.TickFailureLogged,
                    PollCategory.FixedTick => entry.FixedTickFailureLogged,
                    _ => entry.LateTickFailureLogged,
                };
                if (alreadyLogged) return;

                switch (category)
                {
                    case PollCategory.Tick:
                        entry.TickFailureLogged = true;
                        break;
                    case PollCategory.FixedTick:
                        entry.FixedTickFailureLogged = true;
                        break;
                    default:
                        entry.LateTickFailureLogged = true;
                        break;
                }

                _entriesByService[service] = entry;
            }

            LogTickFailure(service, methodName, ex);
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

            entry.TrippedFromPoll = true;

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
                    entry.TickFailureLogged = false;
                    break;
                case PollCategory.FixedTick:
                    if (entry.FixedTickConsecutiveFailures == 0) return;
                    entry.FixedTickConsecutiveFailures = 0;
                    entry.FixedTickFailureLogged = false;
                    break;
                default:
                    if (entry.LateTickConsecutiveFailures == 0) return;
                    entry.LateTickConsecutiveFailures = 0;
                    entry.LateTickFailureLogged = false;
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
