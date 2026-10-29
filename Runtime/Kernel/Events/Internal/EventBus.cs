using System;
using System.Collections.Generic;
using Moirai.Atropos.Kernel.Logging;

namespace Moirai.Atropos.Kernel.Events.Internal
{
    internal sealed class EventBus : IEventBus, IDisposable
    {
        private readonly Dictionary<Type, IDisposable> _channels = new Dictionary<Type, IDisposable>();
        private ILogger _logger;
        private bool _disposed;

        internal EventBus(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {
            EnsureOpen();
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_channels.TryGetValue(typeof(T), out IDisposable existing))
            {
                existing = new Channel<T>(_logger);
                _channels.Add(typeof(T), existing);
            }
            return ((Channel<T>)existing).Add(handler);
        }

        public void Publish<T>(T eventData) where T : class
        {
            EnsureOpen();
            if (eventData == null) throw new ArgumentNullException(nameof(eventData));
            if (_channels.TryGetValue(typeof(T), out IDisposable channel)) ((Channel<T>)channel).Publish(eventData);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (IDisposable channel in _channels.Values) channel.Dispose();
            _channels.Clear();
            _logger = null;
        }

        private void EnsureOpen()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventBus));
        }

        private sealed class Channel<T> : IDisposable where T : class
        {
            private readonly List<Slot> _slots = new List<Slot>();
            private ILogger _logger;
            private int _depth;

            internal Channel(ILogger logger) { _logger = logger; }

            internal IDisposable Add(Action<T> listener)
            {
                var slot = new Slot(listener);
                _slots.Add(slot);
                return slot;
            }

            internal void Publish(T value)
            {
                int limit = _slots.Count;
                ILogger logger = _logger;
                List<Exception> reportingFailures = null;
                _depth++;
                try
                {
                    for (int i = 0; i < limit; i++)
                    {
                        Action<T> listener = _slots[i].Listener;
                        if (listener == null) continue;
                        try { listener(value); }
                        catch (Exception failure)
                        {
                            try { logger.Log(LogLevel.Error, nameof(EventBus), "An event subscriber failed.", failure); }
                            catch (Exception reportingFailure)
                            {
                                if (reportingFailures == null) reportingFailures = new List<Exception>();
                                reportingFailures.Add(failure);
                                reportingFailures.Add(reportingFailure);
                            }
                        }
                    }
                }
                finally
                {
                    if (--_depth == 0)
                    {
                        int write = 0;
                        for (int read = 0; read < _slots.Count; read++)
                            if (_slots[read].Listener != null) _slots[write++] = _slots[read];
                        if (write != _slots.Count) _slots.RemoveRange(write, _slots.Count - write);
                    }
                }
                if (reportingFailures != null) throw new AggregateException(reportingFailures);
            }

            public void Dispose()
            {
                foreach (Slot slot in _slots) slot.Dispose();
                if (_depth == 0) _slots.Clear();
                _logger = null;
            }

            private sealed class Slot : IDisposable
            {
                internal Action<T> Listener;
                internal Slot(Action<T> listener) { Listener = listener; }
                public void Dispose() { Listener = null; }
            }
        }
    }
}
