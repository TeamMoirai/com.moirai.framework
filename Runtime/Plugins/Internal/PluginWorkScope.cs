using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Plugins.Internal
{
    internal sealed class PluginWorkScope
    {
        private readonly HashSet<Ticket> _pending = new HashSet<Ticket>();
        private readonly List<Exception> _errors = new List<Exception>();
        private readonly CancellationTokenSource _running = new CancellationTokenSource();
        private readonly UniTaskCompletionSource<Exception> _stopped = new UniTaskCompletionSource<Exception>();
        private bool _closing;
        private bool _released;

        internal CancellationToken Token => _running.Token;
        internal int InFlight => _pending.Count;

        internal Ticket Begin()
        {
            if (_closing) throw new ObjectDisposedException(nameof(PluginWorkScope));
            var ticket = new Ticket(this);
            _pending.Add(ticket);
            return ticket;
        }

        internal UniTask StopAsync(CancellationToken callerToken)
        {
            if (!_closing)
            {
                _closing = true;
                var snapshot = new Ticket[_pending.Count];
                _pending.CopyTo(snapshot);
                try { _running.Cancel(); }
                catch (Exception failure) { _errors.Add(failure); }
                DrainOwnedAsync(snapshot).Forget(failure => _stopped.TrySetResult(failure));
            }
            return ObserveAsync(callerToken);
        }

        internal async UniTask ReleaseAfterDrainAsync()
        {
            Exception failure = null;
            try { await StopAsync(CancellationToken.None); }
            catch (Exception error) { failure = error; }
            if (!_released)
            {
                _released = true;
                _running.Dispose();
            }
            if (failure != null) throw failure;
        }

        private async UniTask DrainOwnedAsync(Ticket[] snapshot)
        {
            for (int i = 0; i < snapshot.Length; i++) await snapshot[i].Finished.Task;
            _stopped.TrySetResult(_errors.Count == 0 ? null : new AggregateException(_errors));
        }

        private async UniTask ObserveAsync(CancellationToken callerToken)
        {
            Exception failure = await _stopped.Task.AttachExternalCancellation(callerToken);
            if (failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        internal sealed class Ticket
        {
            private PluginWorkScope _owner;
            internal readonly UniTaskCompletionSource Finished = new UniTaskCompletionSource();

            internal Ticket(PluginWorkScope owner) { _owner = owner; }

            internal void Complete(Exception error = null)
            {
                PluginWorkScope owner = _owner;
                if (owner == null) return;
                _owner = null;
                owner._pending.Remove(this);
                if (error != null) owner._errors.Add(error);
                Finished.TrySetResult();
            }
        }
    }
}
