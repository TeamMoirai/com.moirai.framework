using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Persistence
{
    public sealed class InMemorySaveSerializerCatalog : ISaveSerializerCatalog
    {
        private readonly Dictionary<ushort, ISaveSerializer> _serializers =
            new Dictionary<ushort, ISaveSerializer>();
        private readonly Dictionary<Type, SaveWriteDescriptor> _descriptors =
            new Dictionary<Type, SaveWriteDescriptor>();

        public InMemorySaveSerializerCatalog(IEnumerable<ISaveSerializer> serializers,
            IEnumerable<SaveWriteDescriptor> descriptors)
        {
            if (serializers == null) throw new ArgumentNullException(nameof(serializers));
            if (descriptors == null) throw new ArgumentNullException(nameof(descriptors));
            foreach (ISaveSerializer serializer in serializers)
            {
                if (serializer == null) throw new ArgumentException("A serializer cannot be null.");
                if (!_serializers.TryAdd(serializer.BackendId, serializer))
                    throw new InvalidOperationException("Duplicate serializer id: " + serializer.BackendId);
            }
            foreach (SaveWriteDescriptor descriptor in descriptors)
            {
                if (!_serializers.ContainsKey(descriptor.SerializerId))
                    throw new InvalidOperationException("Unknown serializer id: " + descriptor.SerializerId);
                if (!_descriptors.TryAdd(descriptor.DataType, descriptor))
                    throw new InvalidOperationException("Duplicate write descriptor: " + descriptor.DataType);
            }
        }

        public bool TryResolve(ushort persistedId, out ISaveSerializer serializer)
            => _serializers.TryGetValue(persistedId, out serializer);

        public bool TryGetWriteDescriptor(Type dataType, out SaveWriteDescriptor descriptor)
        {
            if (dataType == null) throw new ArgumentNullException(nameof(dataType));
            return _descriptors.TryGetValue(dataType, out descriptor);
        }
    }
}
