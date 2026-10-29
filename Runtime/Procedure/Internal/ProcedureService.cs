using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Kernel.Composition;
using Moirai.Atropos.Kernel.Events;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Procedure.Internal
{
    internal sealed class ProcedureService : IProcedureService
    {
        private readonly IProcedureCatalog _catalog;
        private readonly ProcedureFrameClock _frameClock;
        private readonly IPlayerLoop _loop;
        private readonly ILogger _logger;
        private readonly IEventBus _events;
        private readonly Dictionary<string, IProcedure> _instances =
            new Dictionary<string, IProcedure>(StringComparer.Ordinal);
        private ProcedureMachine _machine;
        private IDisposable _tick;
        private bool _running;
        private bool _stopping;

        internal ProcedureService(IProcedureCatalog catalog, IClock clock, IPlayerLoop loop,
            ILogger logger, IEventBus events)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _frameClock = new ProcedureFrameClock(clock ?? throw new ArgumentNullException(nameof(clock)));
            _loop = loop ?? throw new ArgumentNullException(nameof(loop));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public string CurrentKey => _machine?.CurrentKey;
        public bool IsRunning => _running && _machine != null && _machine.IsRunning;

        public async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_machine != null) return;
            if (string.IsNullOrEmpty(_catalog.EntranceKey))
                throw new InvalidOperationException("A procedure entrance key is required.");
            var owners = new HashSet<IProcedure>();
            foreach (KeyValuePair<string, Func<IProcedureSwitcher, IProcedure>> entry in _catalog.Factories)
            {
                if (entry.Value == null) throw new InvalidOperationException("A procedure factory is null: " + entry.Key);
                IProcedure procedure = entry.Value(this);
                if (procedure == null) throw new InvalidOperationException("A procedure factory returned null: " + entry.Key);
                if (!string.Equals(procedure.Key, entry.Key, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Procedure key '{procedure.Key}' does not match its catalog key '{entry.Key}'.");
                }
                if (!owners.Add(procedure))
                    throw new InvalidOperationException("A procedure instance is registered twice: " + entry.Key);
                _instances.Add(entry.Key, procedure);
            }
            if (!_instances.ContainsKey(_catalog.EntranceKey))
                throw new InvalidOperationException("The procedure entrance is not registered: " + _catalog.EntranceKey);
            var machine = new ProcedureMachine(_instances);
            _machine = machine;
            try { await machine.SwitchToAsync(_catalog.EntranceKey, cancellationToken); }
            catch (Exception failure)
            {
                _machine = null;
                await CleanupAsync(new List<Exception> { failure });
                throw;
            }
            _frameClock.Reset();
            _tick = _loop.AddUpdateListener(OnTick);
            _running = true;
            _events.Publish(new ProcedureEnteredEvent(machine.CurrentKey));
        }

        public async UniTask SwitchToAsync(string procedureKey, CancellationToken cancellationToken)
        {
            if (_stopping) throw new ObjectDisposedException(nameof(ProcedureService));
            if (!_running || _machine == null) throw new InvalidOperationException("The procedure service is not running.");
            string previous = _machine.CurrentKey;
            try { await _machine.SwitchToAsync(procedureKey, cancellationToken); }
            catch (Exception failure)
            {
                _running = false;
                ReleaseTick();
                _logger.Log(LogLevel.Fatal, nameof(ProcedureService),
                    "Procedure transition failed: " + procedureKey, failure);
                _events.Publish(new ProcedureCrashedEvent(procedureKey, failure));
                throw;
            }
            _events.Publish(new ProcedureLeftEvent(previous, false));
            _events.Publish(new ProcedureEnteredEvent(_machine.CurrentKey));
        }

        public async UniTask StopAsync(CancellationToken cancellationToken)
        {
            if (!_stopping)
            {
                _stopping = true;
                _running = false;
                ReleaseTick();
            }
            var failures = new List<Exception>();
            if (_machine != null)
            {
                try { await _machine.StopAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception failure) { failures.Add(failure); }
            }
            await CleanupAsync(failures);
            if (failures.Count != 0) throw new AggregateException(failures);
        }

        private void OnTick()
        {
            if (!_running || _machine == null || !_machine.IsRunning) return;
            _frameClock.Sample(out float elapsed, out float realElapsed);
            try { _machine.Update(elapsed, realElapsed); }
            catch (Exception failure)
            {
                _running = false;
                ReleaseTick();
                _logger.Log(LogLevel.Fatal, nameof(ProcedureService),
                    "Procedure update failed: " + _machine.CurrentKey, failure);
                _events.Publish(new ProcedureCrashedEvent(_machine.CurrentKey, failure));
            }
        }

        private void ReleaseTick()
        {
            IDisposable tick = _tick;
            _tick = null;
            tick?.Dispose();
        }

        private async UniTask CleanupAsync(List<Exception> failures)
        {
            foreach (IProcedure procedure in _instances.Values)
            {
                if (!(procedure is IAsyncDisposableService disposable)) continue;
                try { await disposable.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            _instances.Clear();
            _machine = null;
        }
    }
}
