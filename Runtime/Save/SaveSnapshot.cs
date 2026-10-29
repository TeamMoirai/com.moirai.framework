using System;

namespace Moirai.Atropos.Persistence
{
    public sealed class SaveSnapshot
    {
        private readonly byte[] _bytes;

        public SaveSnapshot(int slot, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            Slot = slot;
            _bytes = (byte[])bytes.Clone();
        }

        public int Slot { get; }
        public ReadOnlyMemory<byte> Bytes => _bytes;
    }
}
