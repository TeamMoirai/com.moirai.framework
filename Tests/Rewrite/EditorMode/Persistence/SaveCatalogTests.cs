using System;
using System.Threading;
using Moirai.Atropos.Persistence;
using NUnit.Framework;

namespace Persistence
{
    public sealed class SaveCatalogTests
    {
        private sealed class TextSerializer : ISaveSerializer
        {
            public ushort BackendId => SaveFormat.JSON;
            public byte[] Serialize<T>(T snapshot, CancellationToken cancellationToken) => new byte[] { 1 };
            public T Deserialize<T>(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
                => default;
        }

        private sealed class OtherSerializer : ISaveSerializer
        {
            public ushort BackendId => SaveFormat.MESSAGE_PACK;
            public byte[] Serialize<T>(T snapshot, CancellationToken cancellationToken) => new byte[] { 2 };
            public T Deserialize<T>(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
                => default;
        }

        private sealed class Payload { }

        private static InMemorySaveSerializerCatalog Create()
        {
            return new InMemorySaveSerializerCatalog(
                new ISaveSerializer[] { new TextSerializer(), new OtherSerializer() },
                new[]
                {
                    new SaveWriteDescriptor(typeof(Payload), "payload", 1, SaveFormat.JSON)
                });
        }

        [Test]
        public void Snapshot_ClonesBytes_AndKeepsSlot()
        {
            var source = new byte[] { 1, 2, 3 };
            var snapshot = new SaveSnapshot(2, source);
            source[0] = 9;
            Assert.AreEqual(2, snapshot.Slot);
            Assert.AreEqual(new byte[] { 1, 2, 3 }, snapshot.Bytes.ToArray());
        }

        [Test]
        public void Resolve_UsesPersistedId_NotWriteConfiguration()
        {
            var catalog = Create();
            Assert.IsTrue(catalog.TryResolve(SaveFormat.JSON, out ISaveSerializer json));
            Assert.AreEqual(SaveFormat.JSON, json.BackendId);
            Assert.IsTrue(catalog.TryResolve(SaveFormat.MESSAGE_PACK, out ISaveSerializer pack));
            Assert.AreEqual(SaveFormat.MESSAGE_PACK, pack.BackendId);
            Assert.IsFalse(catalog.TryResolve(999, out _));
        }

        [Test]
        public void WriteDescriptor_IsBoundPerDataType()
        {
            var catalog = Create();
            Assert.IsTrue(catalog.TryGetWriteDescriptor(typeof(Payload), out SaveWriteDescriptor descriptor));
            Assert.AreEqual("payload", descriptor.BlockKey);
            Assert.AreEqual(SaveFormat.JSON, descriptor.SerializerId);
            Assert.IsFalse(catalog.TryGetWriteDescriptor(typeof(object), out _));
        }

        [Test]
        public void Catalog_RejectsDuplicateAndUnknownSerializerIds()
        {
            Assert.Throws<InvalidOperationException>(() => new InMemorySaveSerializerCatalog(
                new ISaveSerializer[] { new TextSerializer(), new TextSerializer() },
                Array.Empty<SaveWriteDescriptor>()));
            Assert.Throws<InvalidOperationException>(() => new InMemorySaveSerializerCatalog(
                new ISaveSerializer[] { new TextSerializer() },
                new[] { new SaveWriteDescriptor(typeof(Payload), "payload", 1, SaveFormat.PROTOBUF) }));
        }

        [Test]
        public void FormatIds_KeepHistoricalValues()
        {
            Assert.AreEqual(0, SaveFormat.JSON);
            Assert.AreEqual(1, SaveFormat.MESSAGE_PACK);
            Assert.AreEqual(2, SaveFormat.MEMORY_PACK);
            Assert.AreEqual(3, SaveFormat.PROTOBUF);
            Assert.AreEqual(254, SaveFormat.KEY_VALUE);
            Assert.AreEqual(2, SaveFormat.HeaderVersion);
            Assert.AreEqual(2, SaveFormat.ContainerVersion);
            Assert.AreEqual(32, SaveFormat.HeaderSize);
        }
    }
}
