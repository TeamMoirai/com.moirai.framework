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
    /// 泛型处理器槽：优先级排序的 T 处理器挂接表（接口与 Action 共用）。
    /// 拆分自 Slots 文件（钩子表独立——方案 Top3 目标）。
    /// </summary>
        /// <summary>
        /// 单阶段的接口 Handler 注册表：紧凑数组 + 独立的延迟缓冲与失败计数。
        /// </summary>
        /// <remarks>
        /// 数组恒按有效优先级升序（未实现 <see cref="IPlayerLoopPriority"/> 者计 0），同优先级维持注册序； <br />
        /// 末位允许时走 O(1) 追加快路，否则整表稳定排序插入。
        /// </remarks>
        private sealed class HandlerSlot<T> where T : class
        {
            private readonly Action<T, float, float> _invoker;
            private readonly string _stageName;
            private T[] _handlers = new T[INITIAL_CAPACITY];
            private int _count;
            private readonly List<T> _pendingAdd = new List<T>(INITIAL_CAPACITY);
            private readonly List<T> _pendingRemove = new List<T>(INITIAL_CAPACITY);
            private readonly List<T> _sortBuffer = new List<T>(INITIAL_CAPACITY);

            // 连续失败计数懒建：健康路径下 _hasFailures 恒 false，每订户每帧只多读一个 bool
            private bool _hasFailures;
            private Dictionary<T, int> _failures;

            public HandlerSlot(Action<T, float, float> invoker, string stageName)
            {
                _invoker = invoker;
                _stageName = stageName;
            }

            public int Count => _count;

            /// <summary>
            /// 驱动本阶段全部订户，逐个隔离异常，连续失败达阈值即熔断摘出。
            /// </summary>
            /// <remarks>热路径零分配（<c>try</c> 不分配，仅抛出路径走日志与计数）；保留 <c>null</c> 判定以容忍迭代中被 <see cref="Clear"/> 清空。</remarks>
            public void Drive(float arg1, float arg2)
            {
                T[] handlers = _handlers;
                int count = _count;
                // 局部变量阻断编译期可达性折叠——避免 throw 之后的熔断索引补偿触发 CS0162（零运行时差异）
                bool rethrow = RETHROW_SUBSCRIBER_EXCEPTIONS;

                for (int i = 0; i < count; i++)
                {
                    T handler = handlers[i];
                    if (handler == null) continue;

                    try
                    {
                        _invoker(handler, arg1, arg2);
                        ResetFailuresIfAny(handler);
                    }
                    catch (Exception exception)
                    {
                        LogUtility.Fatal("PlayerLoop {0} handler threw: {1}", _stageName, exception);
                        bool tripped = RecordFailure(handler);
                        if (rethrow) throw;
                        // Remove 保序搬移：后继元素左移一位，故回退索引以免跳过，并收缩本地计数
                        if (tripped)
                        {
                            i--;
                            count--;
                        }
                    }
                }
            }

            /// <summary>
            /// 记一次失败；达阈值则把该订户摘出本阶段并告警一次。重新注册即完全重置计数。
            /// </summary>
            private bool RecordFailure(T handler)
            {
                _hasFailures = true;
                _failures ??= new Dictionary<T, int>(INITIAL_CAPACITY);

                int failures = _failures.TryGetValue(handler, out int previous) ? previous + 1 : 1;
                _failures[handler] = failures;
                if (failures < FailureTripThreshold) return false;

                // Remove 内部会一并清除失败计数条目
                Remove(handler);
                LogUtility.Warning(
                    "PlayerLoop {0} handler '{1}' was removed after {2} consecutive failures (threshold {3}).",
                    _stageName, handler.GetType().FullName, failures, FailureTripThreshold);
                return true;
            }

            private void ResetFailuresIfAny(T handler)
            {
                if (!_hasFailures) return;

                if (_failures != null &&
                    _failures.TryGetValue(handler, out int recorded) && recorded != 0)
                {
                    _failures[handler] = 0;
                }
            }

            public void Add(T handler)
            {
                EnsureMainThread();
                if (Contains(handler)) return;

                // 尾部追加仅在「追加后仍满足优先级升序」时合法：未实现 IPlayerLoopPriority 者有效优先级为 0，
                // 若末位已是正优先级，直接追加会把它挤到正优先级之后，违背「数字小者先跑」——此时必须走排序插入。
                if (_count == 0 || GetPriority(_handlers[_count - 1]) <= GetPriority(handler))
                {
                    EnsureCapacity(_count + 1);
                    _handlers[_count++] = handler;
                    return;
                }

                InsertByPriority(handler);
            }

            /// <summary>
            /// 把 <paramref name="handler"/> 并入后按优先级整表稳定排序，写回紧凑数组。
            /// </summary>
            private void InsertByPriority(T handler)
            {
                _sortBuffer.Clear();
                for (int i = 0; i < _count; i++) _sortBuffer.Add(_handlers[i]);
                _sortBuffer.Add(handler);
                SortByPriority(_sortBuffer);

                EnsureCapacity(_sortBuffer.Count);
                _count = _sortBuffer.Count;
                for (int i = 0; i < _count; i++) _handlers[i] = _sortBuffer[i];
                _sortBuffer.Clear();
            }

            public void Remove(T handler)
            {
                EnsureMainThread();

                // 注销即抹除失败计数：重新注册从 0 计起，且字典不再对已注销订户持强引用
                _failures?.Remove(handler);

                for (int i = 0; i < _count; i++)
                {
                    if (!ReferenceEquals(_handlers[i], handler)) continue;

                    // 尾部前移，保持相对顺序
                    for (int j = i; j < _count - 1; j++) _handlers[j] = _handlers[j + 1];
                    _handlers[--_count] = null;
                    return;
                }
            }

            public void AddPending(T handler)
            {
                EnsureMainThread();

                // 同帧 add↔remove 对消（后调用者生效）：撤销未提交的注销；
                // 若本体未激活，仅撤销还不够——仍需排队注册
                for (int i = 0; i < _pendingRemove.Count; i++)
                {
                    if (!ReferenceEquals(_pendingRemove[i], handler)) continue;

                    _pendingRemove.RemoveAt(i);
                    if (!Contains(handler)) _pendingAdd.Add(handler);
                    return;
                }
                _pendingAdd.Add(handler);
            }

            public void RemovePending(T handler)
            {
                EnsureMainThread();

                // 对消同上：撤销未提交的注册；若本体已激活，仍需排队注销
                for (int i = 0; i < _pendingAdd.Count; i++)
                {
                    if (!ReferenceEquals(_pendingAdd[i], handler)) continue;

                    _pendingAdd.RemoveAt(i);
                    if (Contains(handler)) _pendingRemove.Add(handler);
                    return;
                }
                _pendingRemove.Add(handler);
            }

            public void FlushPending()
            {
                int remove = _pendingRemove.Count;
                int add = _pendingAdd.Count;
                if (remove == 0 && add == 0) return;

                // 先注销后注册：对消已保证同帧反向操作不进缓冲，此处仅按「移除再添加」序提交残余项
                for (int i = 0; i < remove; i++) Remove(_pendingRemove[i]);
                _pendingRemove.Clear();

                for (int i = 0; i < add; i++) Add(_pendingAdd[i]);
                _pendingAdd.Clear();
            }

            public void Clear()
            {
                _count = 0;
                Array.Clear(_handlers, 0, _handlers.Length);
                _pendingAdd.Clear();
                _pendingRemove.Clear();
                _sortBuffer.Clear();
                _hasFailures = false;
                _failures = null;
            }

            private bool Contains(T handler)
            {
                for (int i = 0; i < _count; i++)
                {
                    if (ReferenceEquals(_handlers[i], handler)) return true;
                }
                return false;
            }

            private void EnsureCapacity(int required)
            {
                if (required <= _handlers.Length) return;

                int capacity = _handlers.Length;
                while (capacity < required) capacity *= 2;
                Array.Resize(ref _handlers, capacity);
            }

            private static void SortByPriority(List<T> list)
            {
                // 插入排序：稳定性保证同优先级项维持注册序
                for (int i = 1; i < list.Count; i++)
                {
                    T key = list[i];
                    int keyPriority = GetPriority(key);
                    int j = i - 1;
                    while (j >= 0 && GetPriority(list[j]) > keyPriority)
                    {
                        list[j + 1] = list[j];
                        j--;
                    }
                    list[j + 1] = key;
                }
            }

            private static int GetPriority(T handler)
            {
                return handler is IPlayerLoopPriority p ? p.Priority : 0;
            }
        }
    }
}
