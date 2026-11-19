using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 可池化引用事件契约：class 形态事件（带引用负载）实现本契约接入 <see cref="EventPool{TEvent}"/>。
    /// </summary>
    /// <remarks>
    /// <see cref="Reset"/> 在归还池时调用——清空引用负载防止悬垂与内存滞留。
    /// </remarks>
    public interface IPoolableGameEvent
    {
        /// <summary>归还池时清空引用负载（防悬垂/内存滞留）。</summary>
        void Reset();
    }

    /// <summary>
    /// class 事件对象池：带引用负载的大事件保留 class 形态走池，struct 事件（零引用负载）无需池化。
    /// </summary>
    /// <remarks>
    /// 使用模式：Rent → 填充负载 → 包装 struct（持引用）经 <see cref="EventBus.Publish{TEvent}(in TEvent)"/> 派发 → Return。 <br />
    /// 生命周期契约：派发完成后即归还——订阅方不得长期持有事件对象引用（需要留存数据请在回调内拷贝出值）。 <br />
    /// 线程契约：仅限主线程（与总线派发一致）。 <br />
    /// 关 DR 兜底：静态实例随 <see cref="EventChannelCore"/> 的注册链一并清空。
    /// </remarks>
    public static class EventPool<TEvent> where TEvent : class, IPoolableGameEvent, new()
    {
        private static readonly List<TEvent> s_Pool = new List<TEvent>(8);

        /// <summary>当前池中空闲对象数（诊断用）。</summary>
        public static int PooledCount => s_Pool.Count;

        /// <summary>从池取事件对象（池空则构造）。</summary>
        public static TEvent Rent()
        {
            if (s_Pool.Count > 0)
            {
                int last = s_Pool.Count - 1;
                var evt = s_Pool[last];
                s_Pool.RemoveAt(last);
                return evt;
            }
            return new TEvent();
        }

        /// <summary>归还事件对象（Reset 清负载后入池；幂等防御——同一引用重复归还只入池一次）。</summary>
        public static void Return(TEvent evt)
        {
            if (evt == null) return;
            for (int i = 0; i < s_Pool.Count; i++)
            {
                if (ReferenceEquals(s_Pool[i], evt)) return;
            }
            evt.Reset();
            s_Pool.Add(evt);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReload()
        {
            s_Pool.Clear();
        }
    }
}
