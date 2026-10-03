using System;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 序列化后端注册表开放注册测试：注册校验（null/重复/保留标识）、注销语义、内置后端保留。
    /// </summary>
    public class SaveSerializerRegistryTests
    {
        /// <summary>测试用自定义后端标识（按 0..255 框架保留区之外的口径取，避开内置与保留标识）。</summary>
        private const ushort CustomBackend = 1000;

        /// <summary>
        /// 自定义序列化器桩（纯标记实现，不接入块管线）。
        /// </summary>
        /// <remarks>非 <c>[Serializable]</c>：避免被 ProviderDropdown 的类型扫描收进生产资产的候选。</remarks>
        private sealed class StubSerializer : ISaveSerializer
        {
            private readonly ushort _backendId;

            public StubSerializer(ushort backendId)
            {
                _backendId = backendId;
            }

            public ushort BackendId => _backendId;

            public byte[] Serialize<T>(T data)
            {
                return Array.Empty<byte>();
            }

            public T Deserialize<T>(byte[] bytes)
            {
                return default;
            }
        }

        [TearDown]
        public void TearDown()
        {
            // 防御性清理：用例异常也不污染注册表（自定义标识幂等移除；内置后端被移除则补回等价实例）
            SaveSerializerRegistry.Unregister(CustomBackend);
            SaveSerializerRegistry.Unregister((ushort)(SaveBackendIds.RESERVED_MAX + 1));
            if (!SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out _))
            {
                // 内置注册表无法取回原实例，注册等价新实例恢复契约
                SaveSerializerRegistry.Register(new JsonSaveSerializer());
            }
        }

        [Test]
        public void Register_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => SaveSerializerRegistry.Register(null));
        }

        [Test]
        public void Register_DuplicateBackend_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveSerializerRegistry.Register(new StubSerializer(SaveBackendIds.JSON)));
        }

        [Test]
        public void Register_KeyValueBackend_Throws()
        {
            // KeyValue 为组件捕获格式保留标识，禁止外部占用
            Assert.Throws<ArgumentException>(() => SaveSerializerRegistry.Register(new StubSerializer(SaveBackendIds.KEY_VALUE)));
        }

        [Test]
        public void Unregister_KeyValue_ReturnsFalse()
        {
            Assert.IsFalse(SaveSerializerRegistry.Unregister(SaveBackendIds.KEY_VALUE));
        }

        [Test]
        public void Register_FrameworkReservedId_Throws()
        {
            // 保留区内只承认内建标识：将来内置扩号不能与项目已注册的后端静默相撞
            Assert.Throws<ArgumentException>(() => SaveSerializerRegistry.Register(new StubSerializer(4)));
            Assert.Throws<ArgumentException>(() => SaveSerializerRegistry.Register(new StubSerializer(200)));
            Assert.Throws<ArgumentException>(
                () => SaveSerializerRegistry.Register(new StubSerializer(SaveBackendIds.RESERVED_MAX)));
        }

        [Test]
        public void Register_IdJustAboveReservedRange_Succeeds()
        {
            const ushort firstCustomId = (ushort)(SaveBackendIds.RESERVED_MAX + 1);
            Assert.IsFalse(SaveSerializerRegistry.TryGet(firstCustomId, out _), "前置：该 ID 未被登记");

            var stub = new StubSerializer(firstCustomId);
            SaveSerializerRegistry.Register(stub);

            Assert.AreSame(stub, SaveSerializerRegistry.GetRequired(firstCustomId));
            Assert.IsTrue(SaveSerializerRegistry.Unregister(firstCustomId));
        }

        [Test]
        public void Register_CustomBackend_ThenUnregister_RoundTrips()
        {
            Assert.IsFalse(SaveSerializerRegistry.TryGet(CustomBackend, out _), "前置：自定义标识未注册");

            var stub = new StubSerializer(CustomBackend);
            SaveSerializerRegistry.Register(stub);

            Assert.IsTrue(SaveSerializerRegistry.TryGet(CustomBackend, out ISaveSerializer resolved));
            Assert.AreSame(stub, resolved);
            Assert.AreSame(stub, SaveSerializerRegistry.GetRequired(CustomBackend));

            Assert.IsTrue(SaveSerializerRegistry.Unregister(CustomBackend));
            Assert.IsFalse(SaveSerializerRegistry.TryGet(CustomBackend, out _));
            Assert.Throws<GameException>(() => SaveSerializerRegistry.GetRequired(CustomBackend));
            Assert.IsFalse(SaveSerializerRegistry.Unregister(CustomBackend), "重复注销应返回 false");
        }

        [Test]
        public void Register_DuplicateCustomBackend_Throws()
        {
            SaveSerializerRegistry.Register(new StubSerializer(CustomBackend));
            Assert.Throws<ArgumentException>(() => SaveSerializerRegistry.Register(new StubSerializer(CustomBackend)));
        }

        [Test]
        public void Unregister_BuiltIn_GetRequired_FailFast()
        {
            Assert.IsTrue(SaveSerializerRegistry.Unregister(SaveBackendIds.JSON));
            Assert.Throws<GameException>(() => SaveSerializerRegistry.GetRequired(SaveBackendIds.JSON));
        }

        [Test]
        public void BuiltIns_RemainRegistered()
        {
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out _));
#if MESSAGEPACK_INSTALLED
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.MESSAGE_PACK, out _));
#endif
#if MEMORYPACK_INSTALLED
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.MEMORY_PACK, out _));
#endif
#if PROTOBUF_INSTALLED
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.PROTOBUF, out _));
#endif
        }
    }
}
