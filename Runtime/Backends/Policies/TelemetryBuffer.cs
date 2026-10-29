using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Backends.Policies
{
    public sealed class TelemetryBuffer
    {
        private readonly Queue<string> _queued = new Queue<string>();

        public TelemetryBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Capacity { get; }
        public int Count => _queued.Count;
        public long Dropped { get; private set; }

        public bool Enqueue(string eventName)
        {
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (_queued.Count >= Capacity)
            {
                Dropped++;
                return false;
            }
            _queued.Enqueue(eventName);
            return true;
        }

        public bool TryDequeue(out string eventName)
        {
            if (_queued.Count == 0)
            {
                eventName = null;
                return false;
            }
            eventName = _queued.Dequeue();
            return true;
        }

        public void RevokeConsent()
        {
            _queued.Clear();
        }
    }
}
