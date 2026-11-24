using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 诊断与图维护：状态快照、排序与移除工具方法。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="ServiceScope"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal sealed partial class ServiceScope
    {

        /// <summary>
        /// 按注册顺序收集此作用域内已注册服务的诊断信息。
        /// </summary>
        internal void CollectDiagnosticInfo(List<GameServices.DiagnosticInfo> buffer)
        {
            if (IsDisposed) return;

            for (int i = 0; i < _registrationOrder.Count; i++)
            {
                var service = _registrationOrder[i];
                if (service == null || !_entriesByService.TryGetValue(service, out var entry)) continue;

                var type = Type.GetTypeFromHandle(entry.ContractHandles[0]);
                buffer.Add(new GameServices.DiagnosticInfo
                {
                    ContractType = type != null ? type.FullName : "<unknown>",
                    ImplementationType = service.GetType().FullName,
                    Scope = Kind,
                    Priority = service.Priority,
                    HasUpdate = service is IServiceTickable,
                    HasFixedUpdate = service is IServiceFixedTickable,
                    HasLateUpdate = service is IServiceLateTickable,
                    HasGizmo = service is IServiceGizmoDrawable,
                    TrippedFromPoll = entry.TrippedFromPoll,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    PollAvgMs = entry.PollSamples > 0 ? entry.PollTotalMs / entry.PollSamples : 0f,
                    PollPeakMs = entry.PollPeakMs,
                    PollSamples = entry.PollSamples,
#endif
                });
            }
        }

        /// <summary>
        /// 清零本作用域全部服务的轮询耗时统计（不影响失败计数与熔断状态）。
        /// </summary>
        internal void ResetPollStatistics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 遍历 _registrationOrder 而非 _entriesByService——索引器回写会使字典版本号递增，
            // 边遍历边写回同一字典会抛 InvalidOperationException
            for (int i = 0; i < _registrationOrder.Count; i++)
            {
                var service = _registrationOrder[i];
                if (service == null || !_entriesByService.TryGetValue(service, out var entry)) continue;

                if (entry.PollSamples == 0 && entry.PollTotalMs == 0f && entry.PollPeakMs == 0f) continue;

                entry.PollTotalMs = 0f;
                entry.PollPeakMs = 0f;
                entry.PollSamples = 0;
                _entriesByService[service] = entry;
            }
#endif
        }


        // ── lazy-sort：脏标记置位后，下次迭代前排序 + 重建索引 ──

        private void SortTickablesIfDirty()
        {
            if (!_tickablesDirty) return;
            _tickables.Sort(_tickComparison);
            for (int i = 0; i < _tickables.Count; i++)
            {
                var svc = _tickables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
                {
                    e.TickIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _tickablesDirty = false;
        }

        private void SortFixedTickablesIfDirty()
        {
            if (!_fixedTickablesDirty) return;
            _fixedTickables.Sort(_fixedTickComparison);
            for (int i = 0; i < _fixedTickables.Count; i++)
            {
                var svc = _fixedTickables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
                {
                    e.FixedTickIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _fixedTickablesDirty = false;
        }

        private void SortLateTickablesIfDirty()
        {
            if (!_lateTickablesDirty) return;
            _lateTickables.Sort(_lateTickComparison);
            for (int i = 0; i < _lateTickables.Count; i++)
            {
                var svc = _lateTickables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
                {
                    e.LateTickIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _lateTickablesDirty = false;
        }

        private void SortGizmoDrawablesIfDirty()
        {
            if (!_gizmoDrawablesDirty) return;
            _gizmoDrawables.Sort(_gizmoComparison);
            for (int i = 0; i < _gizmoDrawables.Count; i++)
            {
                var svc = _gizmoDrawables[i];
                if (_entriesByService.TryGetValue(svc, out var e))
                {
                    e.GizmoIndex = i;
                    _entriesByService[svc] = e;
                }
            }
            _gizmoDrawablesDirty = false;
        }

        /// <summary>
        /// Priority 降序比较器（高优先在前）；同优先级按 CreationIndex 升序（先注册先执行）。
        /// </summary>
        private int CompareByPriority<T>(T a, T b) where T : class, IService
        {
            int result = b.Priority.CompareTo(a.Priority);
            if (result != 0) return result;

            int leftCreation = GetCreationIndex(a);
            int rightCreation = GetCreationIndex(b);
            return leftCreation.CompareTo(rightCreation);
        }

        private int GetCreationIndex(IService service)
        {
            if (service != null && _entriesByService.TryGetValue(service, out var entry))
                return entry.CreationIndex;
            return int.MaxValue;
        }

        // ── swap-with-last O(1) 移除：末尾元素填补被删位置，更新其索引 ──

        private void RemoveTickableAt(int index)
        {
            int last = _tickables.Count - 1;
            if (index == last) { _tickables.RemoveAt(last); }
            else
            {
                var moved = _tickables[last];
                _tickables[index] = moved;
                _tickables.RemoveAt(last);
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.TickIndex = index;
                    _entriesByService[moved] = e;
                }
            }
            _tickablesDirty = true;
        }

        private void RemoveFixedTickableAt(int index)
        {
            int last = _fixedTickables.Count - 1;
            if (index == last) { _fixedTickables.RemoveAt(last); }
            else
            {
                var moved = _fixedTickables[last];
                _fixedTickables[index] = moved;
                _fixedTickables.RemoveAt(last);
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.FixedTickIndex = index;
                    _entriesByService[moved] = e;
                }
            }
            _fixedTickablesDirty = true;
        }

        private void RemoveLateTickableAt(int index)
        {
            int last = _lateTickables.Count - 1;
            if (index == last) { _lateTickables.RemoveAt(last); }
            else
            {
                var moved = _lateTickables[last];
                _lateTickables[index] = moved;
                _lateTickables.RemoveAt(last);
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.LateTickIndex = index;
                    _entriesByService[moved] = e;
                }
            }
            _lateTickablesDirty = true;
        }

        private void RemoveGizmoDrawableAt(int index)
        {
            int last = _gizmoDrawables.Count - 1;
            if (index == last) { _gizmoDrawables.RemoveAt(last); }
            else
            {
                var moved = _gizmoDrawables[last];
                _gizmoDrawables[index] = moved;
                _gizmoDrawables.RemoveAt(last);
                if (_entriesByService.TryGetValue(moved, out var e))
                {
                    e.GizmoIndex = index;
                    _entriesByService[moved] = e;
                }
            }
            _gizmoDrawablesDirty = true;
        }
    }
}
