using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Kernel.Scheduling
{
    public sealed class CallbackRegistry : IDisposable
    {
        private readonly List<Slot> _slots = new List<Slot>();
        private Action<Exception> _reportFailure;
        private int _depth;
        private bool _disposed;

        public CallbackRegistry(Action<Exception> reportFailure)
        {
            _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        }

        public IDisposable Add(Action listener)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CallbackRegistry));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var slot = new Slot(listener);
            _slots.Add(slot);
            return slot;
        }

        public void Invoke()
        {
            if (_disposed) return;
            int limit = _slots.Count;
            Action<Exception> reportFailure = _reportFailure;
            List<Exception> reportingFailures = null;
            _depth++;
            try
            {
                for (int i = 0; i < limit; i++)
                {
                    Action listener = _slots[i].Listener;
                    if (listener == null) continue;
                    try { listener(); }
                    catch (Exception failure)
                    {
                        try { reportFailure(failure); }
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
            if (_disposed) return;
            _disposed = true;
            foreach (Slot slot in _slots) slot.Dispose();
            if (_depth == 0) _slots.Clear();
            _reportFailure = null;
        }

        private sealed class Slot : IDisposable
        {
            internal Action Listener;
            internal Slot(Action listener) { Listener = listener; }
            public void Dispose() { Listener = null; }
        }
    }
}
