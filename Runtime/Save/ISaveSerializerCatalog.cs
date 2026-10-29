using System;

namespace Moirai.Atropos.Persistence
{
    public interface ISaveSerializerCatalog
    {
        bool TryResolve(ushort persistedId, out ISaveSerializer serializer);
        bool TryGetWriteDescriptor(Type dataType, out SaveWriteDescriptor descriptor);
    }
}
