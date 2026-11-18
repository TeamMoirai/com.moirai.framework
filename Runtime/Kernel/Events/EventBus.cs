using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 总线事件回调委托：in 参数零拷贝（无型变标注——订阅按精确契约类型分桶，无多态场景）。
    /// </summary>
    /// <remarks>
    /// API 习惯决定记录：订阅/退订为对称方法形态（<c>EventBus.Subscribe&lt;T&gt;(h)</c> / <c>EventBus.Unsubscribe&lt;T&gt;(h)</c>， <br />
    /// 与原 EventManager 用法一致），Subscribe 不返回句柄——不采用 IDisposable 句柄/作用域批量退订形态（用户决策）。
    /// </remarks>
    public delegate void EventDelegate<TEvent>(in TEvent evt) where TEvent : struct, IGameEvent;
    
    /// <summary>
    /// 事件总线：跨系统通信的内核管线组件，直接静态调用。
    /// </summary>
    /// <remarks>
    /// struct 事件、in 参数零拷贝、发布期安全队列、作用域批量退订。 <br />
    /// 分桶采用泛型静态容器（<see cref="EventChannel{TEvent}"/>）：每个事件类型一个静态通道实例，零字典查找。
    /// </remarks>
    public static class EventBus
    {
        #region 契约面 [CONTRACT]

        /// <summary>
        /// 订阅事件（与 <see cref="Unsubscribe{TEvent}"/> 成对——保持与原 EventManager 相同的对称 API 习惯）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Subscribe<TEvent>(EventDelegate<TEvent> handler) where TEvent : struct, IGameEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            EventChannel<TEvent>.Instance.Subscribe(handler);
        }

        /// <summary>
        /// 退订事件（幂等；发布期退订被排队，派发循环结束后生效）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unsubscribe<TEvent>(EventDelegate<TEvent> handler) where TEvent : struct, IGameEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            EventChannel<TEvent>.Instance.Unsubscribe(handler);
        }

        /// <summary>
        /// 发布事件（in 参数零拷贝）。同一事件的订阅者按订阅顺序同步派发。
        /// </summary>
        /// <remarks>
        /// 发布期安全：派发前拷贝回调快照——发布期内的订阅本次不派发（下次发布生效）、退订排队后应用。 <br />
        /// 异常分级：逐回调隔离（<see cref="Debug.LogException(System.Exception)"/> 记日志不中断其他订阅者）。 <br />
        /// 重入发布（回调内发布同一事件）直接递归派发——不做防环，由调用方自律。
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish<TEvent>(in TEvent evt) where TEvent : struct, IGameEvent
        {
            EventChannel<TEvent>.Instance.Publish(in evt);
        }

        /// <summary>
        /// 清空指定事件类型的全部订阅（诊断/测试辅助）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Clear<TEvent>() where TEvent : struct, IGameEvent
        {
            EventChannel<TEvent>.Instance.Clear();
        }

        /// <summary>
        /// 关 DR 兜底：清空全部静态状态（进 Play 时重置）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStaticsForPlayModeReset()
        {
            EventChannelCore.ClearAllChannels();
        }

        #endregion
    }
}
