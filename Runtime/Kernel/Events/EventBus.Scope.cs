using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class EventBus
    {
        #region 作用域订阅 [SCOPED SUBSCRIPTIONS]

        // 作用域订阅台账：kind → 退订动作列表（进程级静态，与 EventChannel 泛型静态实例同口径）。
        // 仅主线程读写：登记发生在订阅点、清空发生在容器关停（均主线程契约）。
        private static readonly Dictionary<EServiceScopeKind, List<Action>> s_ScopeUnsubscribers
            = new Dictionary<EServiceScopeKind, List<Action>>();

        /// <summary>
        /// 订阅事件并登记进作用域台账：所属作用域被容器关停（<see cref="ServiceWorld.ShutdownScope"/> / 世界销毁）时自动批量退订。
        /// </summary>
        /// <remarks>
        /// Scene/Gameplay 级订阅者退订遗漏的兜底钩子；<see cref="Subscribe{TEvent}"/> / <see cref="Unsubscribe{TEvent}"/> <br />
        /// 的对称无句柄 API 不受影响——本方法是 opt-in 附加面，不登记的作用域订阅照旧手动成对退订。 <br />
        /// 清空发生在该作用域全部服务 <c>OnShutdown</c> 之后（服务关停体仍可发布事件）；退订幂等， <br />
        /// 先手动退订再清台账不重复触发。
        /// </remarks>
        public static void SubscribeScoped<TEvent>(EServiceScopeKind scope, EventDelegate<TEvent> handler)
            where TEvent : struct, IGameEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            Subscribe(handler);
            if (!s_ScopeUnsubscribers.TryGetValue(scope, out var list))
            {
                list = new List<Action>(4);
                s_ScopeUnsubscribers.Add(scope, list);
            }

            list.Add(() => Unsubscribe(handler));
        }

        /// <summary>
        /// 清空指定作用域的全部登记订阅（幂等静默；容器关停钩子，不对业务暴露）。
        /// </summary>
        internal static void ClearScope(EServiceScopeKind scope)
        {
            if (!s_ScopeUnsubscribers.TryGetValue(scope, out var list)) return;

            s_ScopeUnsubscribers.Remove(scope);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i]();
            }
        }

        /// <summary>清空全部作用域台账（进 Play 时重置）。</summary>
        internal static void ClearAllScopes()
        {
            s_ScopeUnsubscribers.Clear();
        }

        #endregion
    }
}
