using System;

namespace Moirai.Atropos.Input
{
    public enum InputSuppressionOwner : byte { UI = 0, Component = 1 }

    public sealed class InputSuppression
    {
        private readonly int[] _counts = new int[2];

        public bool IsSuppressed => _counts[0] != 0 || _counts[1] != 0;

        public IDisposable Acquire(InputSuppressionOwner owner)
        {
            int index = (int)owner;
            if (index < 0 || index >= _counts.Length)
                throw new ArgumentOutOfRangeException(nameof(owner));
            _counts[index]++;
            return new Lease(this, index);
        }

        private sealed class Lease : IDisposable
        {
            private InputSuppression _owner;
            private readonly int _index;

            internal Lease(InputSuppression owner, int index)
            {
                _owner = owner;
                _index = index;
            }

            public void Dispose()
            {
                InputSuppression owner = _owner;
                if (owner == null) return;
                _owner = null;
                owner._counts[_index]--;
            }
        }
    }
}
