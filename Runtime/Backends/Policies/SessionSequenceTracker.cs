using System;

namespace Moirai.Atropos.Backends.Policies
{
    public sealed class SessionSequenceTracker
    {
        private long _confirmed;

        public SessionSequenceTracker(long confirmedSequence = 0)
        {
            if (confirmedSequence < 0) throw new ArgumentOutOfRangeException(nameof(confirmedSequence));
            _confirmed = confirmedSequence;
        }

        public long Confirmed => _confirmed;

        public bool TryAccept(long sequence)
        {
            if (sequence <= _confirmed) return false;
            if (sequence != _confirmed + 1) return false;
            _confirmed = sequence;
            return true;
        }

        public bool TryAcceptStale(long sequence) => false;
    }
}
