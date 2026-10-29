using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Procedure.Internal
{
    internal sealed class ProcedureMachine
    {
        private readonly IReadOnlyDictionary<string, IProcedure> _procedures;
        private readonly CancellationTokenSource _run = new CancellationTokenSource();
        private IProcedure _current;
        private bool _stopping;
        private bool _faulted;
        private UniTaskCompletionSource<Exception> _active;
        private UniTaskCompletionSource<Exception> _stopped;
        private Exception _transitionFailure;

        internal ProcedureMachine(IReadOnlyDictionary<string, IProcedure> procedures)
        {
            _procedures = procedures ?? throw new ArgumentNullException(nameof(procedures));
        }

        internal string CurrentKey => _current == null ? null : _current.Key;
        internal bool IsRunning => _current != null && !_stopping && !_faulted && _active == null;

        internal UniTask SwitchToAsync(string key, CancellationToken callerToken)
        {
            callerToken.ThrowIfCancellationRequested();
            if (_stopping) throw new ObjectDisposedException(nameof(ProcedureMachine));
            if (_faulted || _active != null)
                throw new InvalidOperationException("A procedure transition is already active or the machine has faulted.");
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!_procedures.TryGetValue(key, out IProcedure next))
                throw new InvalidOperationException("Unknown procedure: " + key);
            var done = new UniTaskCompletionSource<Exception>();
            _active = done;
            TransitionAsync(next, callerToken, done).Forget(failure => done.TrySetResult(failure));
            return AwaitResultAsync(done, callerToken);
        }

        internal void Update(float elapsedSeconds, float realElapsedSeconds)
        {
            if (!IsRunning) return;
            try { _current.OnUpdate(elapsedSeconds, realElapsedSeconds); }
            catch (Exception failure)
            {
                _faulted = true;
                _transitionFailure = failure;
                throw;
            }
        }

        internal UniTask StopAsync(CancellationToken callerToken)
        {
            if (_stopped == null)
            {
                _stopping = true;
                _stopped = new UniTaskCompletionSource<Exception>();
                StopOwnedAsync(_active).Forget(failure => _stopped.TrySetResult(failure));
            }
            return AwaitResultAsync(_stopped, callerToken);
        }

        private async UniTask TransitionAsync(IProcedure next, CancellationToken callerToken,
            UniTaskCompletionSource<Exception> done)
        {
            Exception failure = null;
            CancellationToken operationToken = default;
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_run.Token, callerToken))
                {
                    operationToken = linked.Token;
                    if (_current != null)
                    {
                        await _current.OnLeaveAsync(false, linked.Token);
                        _current = null;
                    }
                    linked.Token.ThrowIfCancellationRequested();
                    _current = next;
                    await next.OnEnterAsync(linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                }
            }
            catch (Exception error)
            {
                failure = error;
                bool cancelledByStop = error is OperationCanceledException cancelled &&
                    cancelled.CancellationToken == operationToken && _stopping &&
                    _run.IsCancellationRequested && !callerToken.IsCancellationRequested;
                _transitionFailure = cancelledByStop ? null : error;
                _faulted = true;
            }
            finally
            {
                _active = null;
                done.TrySetResult(failure);
            }
        }

        private async UniTask StopOwnedAsync(UniTaskCompletionSource<Exception> active)
        {
            var failures = new List<Exception>();
            try
            {
                try { _run.Cancel(); }
                catch (Exception failure) { failures.Add(failure); }
                if (active != null) await active.Task;
                if (_transitionFailure != null) failures.Add(_transitionFailure);
                IProcedure leaving = _current;
                _current = null;
                if (leaving != null)
                {
                    try { await leaving.OnLeaveAsync(true, CancellationToken.None); }
                    catch (Exception failure) { failures.Add(failure); }
                }
            }
            catch (Exception failure) { failures.Add(failure); }
            finally
            {
                _run.Dispose();
                _stopped.TrySetResult(failures.Count == 0 ? null : new AggregateException(failures));
            }
        }

        private static async UniTask AwaitResultAsync(UniTaskCompletionSource<Exception> done,
            CancellationToken callerToken)
        {
            Exception failure = await done.Task.AttachExternalCancellation(callerToken);
            if (failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
