using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Bootstrap.Internal;
using Moirai.Atropos.Kernel.Composition;

namespace Moirai.Atropos.Bootstrap
{
    public sealed class GameRoot
    {
        private readonly Func<IServiceModule[]> _buildModules;
        private readonly UniTaskCompletionSource<Exception> _started = new UniTaskCompletionSource<Exception>();
        private readonly UniTaskCompletionSource _stopped = new UniTaskCompletionSource();
        private ServiceContainer _container;
        private BootPhase _phase = BootPhase.NotStarted;
        private bool _startInvoked;
        private bool _closeInvoked;
        private Exception _startFailure;
        private readonly List<Exception> _failures = new List<Exception>();

        public GameRoot(Func<IServiceModule[]> buildModules)
        {
            _buildModules = buildModules ?? throw new ArgumentNullException(nameof(buildModules));
        }

        public GameRoot(IServiceModule[] modules)
            : this(() => modules ?? throw new ArgumentNullException(nameof(modules))) { }

        public BootPhase Phase => _phase;

        public UniTask StartAsync(CancellationToken cancellationToken)
        {
            if (_startInvoked)
            {
                if (_phase == BootPhase.Failed || _phase == BootPhase.Stopping || _phase == BootPhase.Stopped)
                    throw new ObjectDisposedException(nameof(GameRoot));
                return ObserveAsync(_started, cancellationToken, true);
            }
            _startInvoked = true;
            StartOwnedAsync().Forget(failure => _started.TrySetResult(failure));
            return ObserveAsync(_started, cancellationToken, true);
        }

        public UniTask ShutdownAsync(CancellationToken cancellationToken)
        {
            if (!_closeInvoked)
            {
                _closeInvoked = true;
                ShutdownOwnedAsync().Forget(_ => _stopped.TrySetResult());
            }
            return ObserveAsync(_stopped, cancellationToken, false);
        }

        private async UniTask StartOwnedAsync()
        {
            Exception failure = null;
            try
            {
                SetPhase(BootPhase.Building);
                _container = new ServiceContainer(_buildModules());
                SetPhase(BootPhase.Initializing);
                await _container.InitializeAsync(CancellationToken.None);
                SetPhase(BootPhase.StartingProcedures);
                SetPhase(BootPhase.Ready);
            }
            catch (Exception error)
            {
                failure = error;
                _startFailure = error;
                _failures.Add(error);
                SetPhase(BootPhase.Failed);
                _started.TrySetResult(error);
                await ShutdownAsync(CancellationToken.None);
                return;
            }
            _started.TrySetResult(null);
        }

        private async UniTask ShutdownOwnedAsync()
        {
            if (_startFailure == null) SetPhase(BootPhase.Stopping);
            if (_startInvoked) await _started.Task.AttachExternalCancellation(CancellationToken.None);
            if (_container != null)
            {
                try { await _container.ShutdownAsync(CancellationToken.None); }
                catch (Exception failure) { _failures.Add(failure); }
            }
            if (_startFailure == null) SetPhase(BootPhase.Stopped);
            _stopped.TrySetResult();
        }

        private void SetPhase(BootPhase phase)
        {
            if (_phase == BootPhase.Failed && phase == BootPhase.Stopped) return;
            _phase = phase;
        }

        private static async UniTask ObserveAsync(UniTaskCompletionSource<Exception> completion,
            CancellationToken cancellationToken, bool rethrowFailure)
        {
            Exception failure = await completion.Task.AttachExternalCancellation(cancellationToken);
            if (rethrowFailure && failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static async UniTask ObserveAsync(UniTaskCompletionSource completion,
            CancellationToken cancellationToken, bool rethrowFailure)
        {
            await completion.Task.AttachExternalCancellation(cancellationToken);
        }
    }
}
