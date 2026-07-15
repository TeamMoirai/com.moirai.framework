#if R3_INSTALLED
using System;
using System.Threading;
using Moirai.Atropos.Events;
using R3;

namespace Moirai.Atropos.R3
{
    public static class ObservableExtensions
    {
        #region CallbackEventHandler
        
        /// <summary>
        /// 为 <see cref="CallbackEventHandler"/> 创建 <see cref="Observable{TEventType}"/>
        /// </summary>
        /// <param name="handler"></param>
        /// <typeparam name="TEventType"></typeparam>
        /// <returns></returns>
        public static Observable<TEventType> AsObservable<TEventType>(this CallbackEventHandler handler)
            where TEventType : EventBase<TEventType>, new()
        {
            return handler.AsObservable<TEventType>(TrickleDown.NoTrickleDown);
        }
        
        /// <summary>
        /// 为 <see cref="CallbackEventHandler"/> 创建 Observable 对象
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="trickleDown"></param>
        /// <typeparam name="TEventType"></typeparam>
        /// <returns></returns>
        public static Observable<TEventType> AsObservable<TEventType>(this CallbackEventHandler handler, TrickleDown trickleDown)
            where TEventType : EventBase<TEventType>, new()
        {
            CancellationToken cancellationToken = default;
            if (handler is IBehaviourScope behaviourScope && behaviourScope.Behaviour)
                cancellationToken = behaviourScope.Behaviour.destroyCancellationToken;
            return new FromEventHandler<TEventType>(static action => new EventCallback<TEventType>(action),
                callback => handler.RegisterCallback(callback, trickleDown), callback => handler.UnregisterCallback(callback, trickleDown), cancellationToken);
        }
        
        #endregion
        
        /// <summary>
        /// 订阅 <see cref="Observable{TEventType}"/> 最后 Dispose 事件，为 <see cref="EventBase"/> 提供更好的性能
        /// </summary>
        /// <param name="source"></param>
        /// <param name="onNext"></param>
        /// <typeparam name="TEventType"></typeparam>
        /// <returns></returns>
        [StackTraceFrame]
        public static IDisposable SubscribeSafe<TEventType>(this Observable<TEventType> source, EventCallback<TEventType> onNext) where TEventType : EventBase<TEventType>, new()
        {
            var action = new Action<TEventType>(OnNext);
            return source.Subscribe(action);

            void OnNext(TEventType evt)
            {
                onNext(evt);
                evt.Dispose();
            }
        }
    }
}
#endif