using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 总线诊断钩子：仅编辑器存在——生产/开发构建编译期剥除，发布路径零诊断开销。
    /// 独立的编辑器调试窗（Editor/Kernel/Events/EventBusDebuggerWindow）经 InternalsVisibleTo 挂接本钩子，
    /// 不占用 Debugger 服务窗口注册表，内核侧不引入任何调试接口。
    /// </summary>
#if UNITY_EDITOR
    internal static class EventBusDiagnostics
    {
        /// <summary>
        /// 每次事件派发触发（参数：事件类型 + 事件值快照）。
        /// 快照为 struct 事件的装箱副本（仅当调试窗挂接时求值——<c>?.</c> 在未挂接时短路，零装箱）。
        /// </summary>
        internal static Action<Type, object> OnPublished;
    }
#endif
    
    /// <summary>
    /// 单事件类型的订阅通道（泛型静态容器）：每个 <typeparamref name="TEvent"/> 一个静态实例，零字典查找。
    /// </summary>
    /// <typeparam name="TEvent">事件类型（struct，实现 <see cref="IGameEvent"/>）。</typeparam>
    public sealed class EventChannel<TEvent> where TEvent : struct, IGameEvent
    {
        /// <summary>泛型静态实例：每个事件类型天然一份（JIT 按 TEvent 实例化泛型类）。</summary>
        public static readonly EventChannel<TEvent> Instance = new EventChannel<TEvent>();

        // 订阅桶：派发顺序即订阅序；快照拷贝保证发布期安全
        private readonly List<EventDelegate<TEvent>> _handlers = new List<EventDelegate<TEvent>>(4);

        // 快照缓存数组：容量足够时复用（稳态发布零分配），仅扩容时分配
        private EventDelegate<TEvent>[] _snapshot;

        // 发布期深度的待处理变更队列
        private readonly List<PendingUnsubscribe> _pendingUnsubscribes = new List<PendingUnsubscribe>();
        private int _publishingDepth;

        static EventChannel()
        {
            // 自报清空回调：关 DR 兜底（ResetStaticsForPlayModeReset）需要能枚举全部泛型静态实例
            EventChannelCore.Register(() => Instance.Clear());
        }

        private EventChannel() { }

        /// <summary>
        /// 订阅（订阅顺序即派发顺序；发布期内订阅直接入桶，本次事件经快照不派发）。
        /// </summary>
        public void Subscribe(EventDelegate<TEvent> handler)
        {
            _handlers.Add(handler);
        }

        /// <summary>
        /// 退订（幂等；发布期排队）。
        /// </summary>
        public void Unsubscribe(EventDelegate<TEvent> handler)
        {
            if (_publishingDepth > 0)
            {
                _pendingUnsubscribes.Add(new PendingUnsubscribe(handler));
                return;
            }

            UnsubscribeCore(handler);
        }

        /// <summary>
        /// 发布（快照派发 + 逐回调异常隔离 + 待处理退订在循环结束后应用）。
        /// </summary>
        public void Publish(in TEvent evt)
        {
            if (_handlers.Count == 0)
                return;

#if UNITY_EDITOR
            // 诊断钩子每次发布仅触发一次（非每订阅者）；evt 装箱仅在调试窗挂接时发生（?. 短路保证零挂接时零开销）
            EventBusDiagnostics.OnPublished?.Invoke(typeof(TEvent), evt);
#endif

            // 派发前拷贝回调快照：发布期内的订阅/退订不影响本循环。
            // 快照缓存数组复用——稳态发布零分配，仅扩容或重入发布时分配：
            // 嵌套 Publish（外层 handler 里再发布）若复用同一数组，内层拷贝会覆盖外层尚未派发到的元素，
            // 破坏外层循环语义，故 _publishingDepth > 0 时一律走新数组（重入是小概率路径，不为它保稳态零分配）。
            int count = _handlers.Count;
            if (_publishingDepth > 0 || _snapshot == null || _snapshot.Length < count)
            {
                int newSize = Mathf.NextPowerOfTwo(count > 4 ? count : 4);
                _snapshot = new EventDelegate<TEvent>[newSize];
            }
            var snapshot = _snapshot;
            for (int i = 0; i < count; i++)
                snapshot[i] = _handlers[i];

            _publishingDepth++;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var handler = snapshot[i];
                    if (handler == null) continue;

                    try
                    {
                        handler(in evt);
                    }
                    catch (Exception e)
                    {
                        // 异常分级（与 EventDispatchPolicy 同口径）：RETHROW 门控开=开发期上抛（调试器断在抛点）；关=发布期隔离（逐回调记日志不中断其他订阅者）
                        if (EventDispatchPolicy.RETHROW_DISPATCH_EXCEPTIONS) throw;
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                _publishingDepth--;
                if (_publishingDepth == 0 && _pendingUnsubscribes.Count > 0)
                {
                    for (int i = 0; i < _pendingUnsubscribes.Count; i++)
                    {
                        _pendingUnsubscribes[i].Apply(this);
                    }

                    _pendingUnsubscribes.Clear();
                }
            }
        }

        /// <summary>
        /// 清空全部订阅（诊断/测试辅助；跨场景清理由订阅方在场景卸载回调中成对 Unsubscribe）。
        /// </summary>
        public void Clear()
        {
            _handlers.Clear();
            _pendingUnsubscribes.Clear();
        }

        private void UnsubscribeCore(EventDelegate<TEvent> handler)
        {
            for (int i = 0; i < _handlers.Count; i++)
            {
                // 值等价匹配（同方法同目标即相等）——方法组转委托每次生成新实例，引用比较永不匹配
                if (Equals(_handlers[i], handler))
                {
                    _handlers.RemoveAt(i);
                    return;
                }
            }
        }

        private readonly struct PendingUnsubscribe
        {
            private readonly EventDelegate<TEvent> _handler;

            public PendingUnsubscribe(EventDelegate<TEvent> handler)
            {
                _handler = handler;
            }

            public void Apply(EventChannel<TEvent> channel)
            {
                channel.UnsubscribeCore(_handler);
            }
        }
    }

    /// <summary>
    /// 通道注册表：关 DR 兜底时清空全部类型通道（泛型静态实例的枚举入口）。
    /// </summary>
    internal static class EventChannelCore
    {
        // 泛型静态实例无法被外部枚举（JIT per-type 实例化）——ClearAllChannels 通过各类型的注册回调实现：
        // EventChannel<T> 静态构造时自报，运行时收集到的通道在 ResetStaticsForPlayModeReset 里逐个清空
        private static readonly List<Action> s_ChannelClearers = new List<Action>();

        internal static void Register(Action clearer) => s_ChannelClearers.Add(clearer);

        internal static void ClearAllChannels()
        {
            // 只清空各通道订阅——注册表保留（泛型静态实例生命周期内有效，后续重置仍需枚举它们）
            for (int i = 0; i < s_ChannelClearers.Count; i++)
            {
                s_ChannelClearers[i]();
            }
        }
    }
}
