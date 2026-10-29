using System;

namespace Moirai.Atropos.Persistence
{
    public readonly struct SaveWriteDescriptor
    {
        public SaveWriteDescriptor(Type dataType, string blockKey, int dataVersion, ushort serializerId)
        {
            DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
            BlockKey = blockKey ?? throw new ArgumentNullException(nameof(blockKey));
            DataVersion = dataVersion;
            SerializerId = serializerId;
        }

        public Type DataType { get; }
        public string BlockKey { get; }
        public int DataVersion { get; }
        public ushort SerializerId { get; }
    }
}
