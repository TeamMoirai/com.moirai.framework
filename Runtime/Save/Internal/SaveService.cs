using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Storage;

namespace Moirai.Atropos.Persistence.Internal
{
    internal sealed class SaveService
    {
        private readonly ISaveSerializerCatalog _catalog;
        private readonly SlotOperationQueue _queue;
        private readonly IFileSystem _files;
        private readonly Func<int, string> _path;

        internal SaveService(ISaveSerializerCatalog catalog, IFileSystem files, Func<int, string> path)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _queue = new SlotOperationQueue(files, path);
        }

        internal UniTask<SaveCommit> CommitAsync<T>(int slot, T data,
            CancellationToken cancellationToken) where T : class
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!_catalog.TryGetWriteDescriptor(typeof(T), out SaveWriteDescriptor descriptor))
                throw new SaveException(SaveError.InvalidFormat, "No write descriptor for " + typeof(T));
            if (!_catalog.TryResolve(descriptor.SerializerId, out ISaveSerializer serializer))
                throw new SaveException(SaveError.UnknownSerializer, "Unknown serializer " + descriptor.SerializerId);
            byte[] bytes = serializer.Serialize(data, cancellationToken);
            return _queue.EnqueueAsync(slot, bytes, cancellationToken);
        }

        internal async UniTask<T> ReadAsync<T>(int slot, ushort persistedId,
            CancellationToken cancellationToken) where T : class
        {
            if (!_catalog.TryResolve(persistedId, out ISaveSerializer serializer))
                throw new SaveException(SaveError.UnknownSerializer, "Unknown serializer " + persistedId);
            byte[] bytes = await _files.ReadAllBytesAsync(_path(slot), cancellationToken);
            return serializer.Deserialize<T>(bytes, cancellationToken);
        }
    }
}
