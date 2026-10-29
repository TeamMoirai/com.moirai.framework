using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Kernel.Unity
{
    public sealed class UnityLifecycleEvents : ILifecycleEvents, IAsyncInitializable, IAsyncStoppable, IDisposable
    {
        private readonly List<Action<bool>> _focus = new List<Action<bool>>();
        private readonly List<Action> _quit = new List<Action>();
        private readonly ILogger _logger;
        private bool _initialized;
        private bool _disposed;

        public UnityLifecycleEvents(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public event Action<bool> ApplicationFocus
        {
            add { EnsureOpen(); _focus.Add(value); }
            remove { _focus.Remove(value); }
        }

        public event Action ApplicationQuit
        {
            add { EnsureOpen(); _quit.Add(value); }
            remove { _quit.Remove(value); }
        }

        public UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed) throw new ObjectDisposedException(nameof(UnityLifecycleEvents));
            if (_initialized) return UniTask.CompletedTask;
            UnityEngine.Application.focusChanged += OnFocus;
            UnityEngine.Application.quitting += OnQuit;
            _initialized = true;
            return UniTask.CompletedTask;
        }

        public UniTask StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_initialized)
            {
                UnityEngine.Application.focusChanged -= OnFocus;
                UnityEngine.Application.quitting -= OnQuit;
                _initialized = false;
            }
            _focus.Clear();
            _quit.Clear();
        }

        private void OnFocus(bool focused) => Deliver(_focus, focused);
        private void OnQuit() => Deliver(_quit);

        private void Deliver(List<Action> listeners)
        {
            var snapshot = listeners.ToArray();
            foreach (Action listener in snapshot)
            {
                try { listener(); }
                catch (Exception failure) { Report(failure); }
            }
        }

        private void Deliver(List<Action<bool>> listeners, bool focused)
        {
            var snapshot = listeners.ToArray();
            foreach (Action<bool> listener in snapshot)
            {
                try { listener(focused); }
                catch (Exception failure) { Report(failure); }
            }
        }

        private void Report(Exception failure)
        {
            _logger.Log(LogLevel.Error, nameof(UnityLifecycleEvents), "A lifecycle subscriber failed.", failure);
        }

        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnityLifecycleEvents));
        }
    }
}
