using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 阶段注册表：PlayerLoop 阶段插槽（SubSystem 挂接）与生命周期（Init/Shutdown）。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="PlayerLoopDriver"/>——公共 API 零变化（文件级单一职责）。</remarks>
    internal static partial class PlayerLoopDriver
    {
    /// <summary>
    /// 回调槽：PlayerLoop 阶段挂接的回调容器与插拔管理。
    /// 拆分自 Slots 文件（钩子表独立）。
    /// </summary>
        /// <summary>
        /// 单阶段的 Action 回调注册表（语义同 <see cref="HandlerSlot{T}"/>，无优先级）。
        /// </summary>
        private sealed class CallbackSlot
        {
            private readonly string _stageName;
            private Action[] _callbacks = new Action[INITIAL_CAPACITY];
            private int _count;
            private readonly List<Action> _pendingAdd = new List<Action>(INITIAL_CAPACITY);
            private readonly List<Action> _pendingRemove = new List<Action>(INITIAL_CAPACITY);

            private bool _hasFailures;
            private Dictionary<Action, int> _failures;

            public CallbackSlot(string stageName)
            {
                _stageName = stageName;
            }

            public int Count => _count;

            /// <summary>
            /// 驱动本阶段全部回调：隔离与熔断语义同 <see cref="HandlerSlot{T}.Drive"/>。
            /// </summary>
            public void Drive()
            {
                Action[] callbacks = _callbacks;
                int count = _count;
                bool rethrow = RETHROW_SUBSCRIBER_EXCEPTIONS;

                for (int i = 0; i < count; i++)
                {
                    Action callback = callbacks[i];
                    if (callback == null) continue;

                    try
                    {
                        callback();
                        ResetFailuresIfAny(callback);
                    }
                    catch (Exception exception)
                    {
                        LogUtility.Fatal("PlayerLoop {0} callback threw: {1}", _stageName, exception);
                        bool tripped = RecordFailure(callback);
                        if (rethrow) throw;
                        if (tripped)
                        {
                            i--;
                            count--;
                        }
                    }
                }
            }

            private bool RecordFailure(Action callback)
            {
                _hasFailures = true;
                _failures ??= new Dictionary<Action, int>(INITIAL_CAPACITY);

                int failures = _failures.TryGetValue(callback, out int previous) ? previous + 1 : 1;
                _failures[callback] = failures;
                if (failures < FailureTripThreshold) return false;

                // Remove 内部会一并清除失败计数条目
                Remove(callback);
                LogUtility.Warning(
                    "PlayerLoop {0} callback '{1}.{2}' was removed after {3} consecutive failures (threshold {4}).",
                    _stageName, callback.Method.DeclaringType, callback.Method.Name,
                    failures, FailureTripThreshold);
                return true;
            }

            private void ResetFailuresIfAny(Action callback)
            {
                if (!_hasFailures) return;

                if (_failures != null &&
                    _failures.TryGetValue(callback, out int recorded) && recorded != 0)
                {
                    _failures[callback] = 0;
                }
            }

            public void Add(Action callback)
            {
                EnsureMainThread();
                if (Contains(callback)) return;

                EnsureCapacity(_count + 1);
                _callbacks[_count++] = callback;
            }

            public void Remove(Action callback)
            {
                EnsureMainThread();

                // 注销即抹除失败计数：重新注册从 0 计起，且字典不再对已注销订户持强引用
                _failures?.Remove(callback);

                for (int i = 0; i < _count; i++)
                {
                    if (_callbacks[i] != callback) continue;

                    for (int j = i; j < _count - 1; j++) _callbacks[j] = _callbacks[j + 1];
                    _callbacks[--_count] = null;
                    return;
                }
            }

            public void AddPending(Action callback)
            {
                EnsureMainThread();

                // 同帧 add↔remove 对消（后调用者生效）：撤销未提交的注销；
                // 若本体未激活，仅撤销还不够——仍需排队注册
                for (int i = 0; i < _pendingRemove.Count; i++)
                {
                    if (_pendingRemove[i] != callback) continue;

                    _pendingRemove.RemoveAt(i);
                    if (!Contains(callback)) _pendingAdd.Add(callback);
                    return;
                }
                _pendingAdd.Add(callback);
            }

            public void RemovePending(Action callback)
            {
                EnsureMainThread();

                // 对消同上：撤销未提交的注册；若本体已激活，仍需排队注销
                for (int i = 0; i < _pendingAdd.Count; i++)
                {
                    if (_pendingAdd[i] != callback) continue;

                    _pendingAdd.RemoveAt(i);
                    if (Contains(callback)) _pendingRemove.Add(callback);
                    return;
                }
                _pendingRemove.Add(callback);
            }

            public void FlushPending()
            {
                int remove = _pendingRemove.Count;
                int add = _pendingAdd.Count;
                if (remove == 0 && add == 0) return;

                for (int i = 0; i < remove; i++) Remove(_pendingRemove[i]);
                _pendingRemove.Clear();

                for (int i = 0; i < add; i++) Add(_pendingAdd[i]);
                _pendingAdd.Clear();
            }

            public void Clear()
            {
                _count = 0;
                Array.Clear(_callbacks, 0, _callbacks.Length);
                _pendingAdd.Clear();
                _pendingRemove.Clear();
                _hasFailures = false;
                _failures = null;
            }

            private bool Contains(Action callback)
            {
                for (int i = 0; i < _count; i++)
                {
                    if (_callbacks[i] == callback) return true;
                }
                return false;
            }

            private void EnsureCapacity(int required)
            {
                if (required <= _callbacks.Length) return;

                int capacity = _callbacks.Length;
                while (capacity < required) capacity *= 2;
                Array.Resize(ref _callbacks, capacity);
            }
        }
    }
}
