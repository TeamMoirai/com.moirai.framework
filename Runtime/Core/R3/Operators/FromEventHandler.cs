#if R3_INSTALLED
using System;
using System.Threading;
using Moirai.Atropos.Events;
using R3;

namespace Moirai.Atropos.R3
{
    internal sealed class FromEventHandler<T> : Observable<T> where T : EventBase<T>, new()
    {
        private readonly Func<Action<T>, EventCallback<T>> _conversion;
        private readonly Action<EventCallback<T>> _addHandler;
        private readonly Action<EventCallback<T>> _removeHandler;
        private readonly CancellationToken _cancellationToken;
        
        public FromEventHandler(Func<Action<T>, EventCallback<T>> conversion, Action<EventCallback<T>> addHandler, Action<EventCallback<T>> removeHandler, CancellationToken cancellationToken)
        {
            _conversion = conversion; ;
            _addHandler = addHandler;
            _removeHandler = removeHandler;
            _cancellationToken = cancellationToken;
        }
        
        protected override IDisposable SubscribeCore(Observer<T> observer)
        {
            return new FromEventHandlerPattern(_conversion, _addHandler, _removeHandler, observer, _cancellationToken);
        }
        
#nullable enable
        sealed class FromEventHandlerPattern : IDisposable
        {
            private Observer<T>? _observer;
            private Action<EventCallback<T>>? _removeHandler;
            private readonly EventCallback<T> _registeredHandler;
            private CancellationTokenRegistration _cancellationTokenRegistration;
            
            public FromEventHandlerPattern(Func<Action<T>, EventCallback<T>> conversion, Action<EventCallback<T>> addHandler, Action<EventCallback<T>> removeHandler, Observer<T> observer, CancellationToken cancellationToken)
            {
                _observer = observer;
                _removeHandler = removeHandler;
                _registeredHandler = conversion(OnNext);
                addHandler(_registeredHandler);

                if (cancellationToken.CanBeCanceled)
                {
                    _cancellationTokenRegistration = cancellationToken.Register(static state =>
                    {
                        var s = (FromEventHandlerPattern)state!;
                        s.CompleteDispose();
                    }, this, false);
                }
            }
            
            private void OnNext(T value)
            {
                // 防止 eventBase 池化 => 需要手动调用 dispose 一次
                value.Acquire();
                _observer?.OnNext(value);
            }

            private void CompleteDispose()
            {
                _observer?.OnCompleted();
                Dispose();
            }

            public void Dispose()
            {
                var handler = Interlocked.Exchange(ref _removeHandler, null);
                if (handler != null)
                {
                    _observer = null;
                    _removeHandler = null;
                    _cancellationTokenRegistration.Dispose();
                    handler(_registeredHandler);
                }
            }
        }
#nullable disable
    }
}
#endif