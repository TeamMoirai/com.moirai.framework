using System;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 序列化后端注册表测试：注册校验（null/重复/保留标识）、泛型重载注册、注销语义（内置记录不复活、热替换）、内置后端按需实例化且复用同一实例。
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

        /// <summary>
        /// 泛型注册用序列化器桩（无参构造，满足 <c>Register&lt;T&gt;()</c> 的 <c>new()</c> 约束）。
        /// </summary>
        /// <remarks>同样不带 <c>[Serializable]</c>，避免被 ProviderDropdown 的类型扫描收进候选。</remarks>
        private sealed class CustomBackendSerializer : ISaveSerializer
        {
            public ushort BackendId => CustomBackend;

            public byte[] Serialize<T>(T data)
            {
                return Array.Empty<byte>();
            }

            public T Deserialize<T>(byte[] bytes)
            {
                return default;
            }
        }

        /// <summary>
        /// 声明式登记的后来者桩（与 <see cref="CustomBackendSerializer"/> 争同一标识用）。
        /// </summary>
        private sealed class LateBackendSerializer : ISaveSerializer
        {
            public ushort BackendId => 1001;

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
            // 防御性清理：用例异常也不污染注册表（自定义标识幂等移除；内置后端被注销或换过实现则补回内置实例）
            SaveSerializerRegistry.Unregister(CustomBackend);
            SaveSerializerRegistry.Unregister((ushort)(SaveBackendIds.RESERVED_MAX + 1));
            if (!SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out ISaveSerializer json) || !(json is JsonSaveSerializer))
            {
                // 内置类型记录已被注销摘除，注册表不会自己长回来，须显式补一份等价实例
                SaveSerializerRegistry.Unregister(SaveBackendIds.JSON);
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
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out _), "前置：内置后端可解析");

            Assert.IsTrue(SaveSerializerRegistry.Unregister(SaveBackendIds.JSON));
            // 注销要连内置类型记录一起摘掉：只删实例的话，下一次查询会按那张表把它悄悄重建出来
            Assert.IsFalse(SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out _), "注销后不得按内置类型表复活");
            Assert.Throws<GameException>(() => SaveSerializerRegistry.GetRequired(SaveBackendIds.JSON));
        }

        [Test]
        public void Unregister_BuiltIn_Register_SwapsImplementation()
        {
            Assert.IsTrue(SaveSerializerRegistry.Unregister(SaveBackendIds.JSON));

            var stub = new StubSerializer(SaveBackendIds.JSON);
            SaveSerializerRegistry.Register(stub);

            Assert.AreSame(stub, SaveSerializerRegistry.GetRequired(SaveBackendIds.JSON), "注销后同标识可换实现（热替换口径）");
        }

        [Test]
        public void TryGet_BuiltIn_ReusesOneInstancePerBackend()
        {
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out ISaveSerializer first));
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out ISaveSerializer second));

            Assert.IsInstanceOf<JsonSaveSerializer>(first);
            Assert.AreSame(first, second, "按需实例化后同一后端须复用同一实例");
        }

        [Test]
        public void Register_GenericOverload_ServesItsOwnBackendId()
        {
            Assert.IsFalse(SaveSerializerRegistry.TryGet(CustomBackend, out _), "前置：该 ID 未被登记");

            SaveSerializerRegistry.Register<CustomBackendSerializer>();

            Assert.IsTrue(SaveSerializerRegistry.TryGet(CustomBackend, out ISaveSerializer resolved));
            Assert.IsInstanceOf<CustomBackendSerializer>(resolved);
        }

        [Test]
        public void RegisterType_DuplicatePendingDeclaration_KeepsFirstWithoutThrowing()
        {
            UtfLogExpect.Error();
            SaveSerializerRegistry.Register(CustomBackend, typeof(CustomBackendSerializer));

            // 声明式登记跑在模块初始化期，抛出会连累整个编辑器：撞号只能记 Fatal 并保留先到那份
            Assert.DoesNotThrow(() => SaveSerializerRegistry.Register(CustomBackend, typeof(LateBackendSerializer)));
            Assert.IsTrue(SaveSerializerRegistry.TryGet(CustomBackend, out ISaveSerializer serving));
            Assert.IsInstanceOf<CustomBackendSerializer>(serving, "后来者不得顶掉先到的类型登记");
        }

        [Test]
        public void RegisterType_DuplicateAfterInstantiation_KeepsFirstWithoutThrowing()
        {
            UtfLogExpect.Error();
            SaveSerializerRegistry.Register(CustomBackend, typeof(CustomBackendSerializer));
            Assert.IsTrue(SaveSerializerRegistry.TryGet(CustomBackend, out _), "前置：先到那份已完成首次实例化");

            Assert.DoesNotThrow(() => SaveSerializerRegistry.Register(CustomBackend, typeof(LateBackendSerializer)));
            Assert.IsTrue(SaveSerializerRegistry.TryGet(CustomBackend, out ISaveSerializer serving));
            Assert.IsInstanceOf<CustomBackendSerializer>(serving, "实例已就位时同样保留先到那份");
        }

        [Test]
        public void RegisterType_ReservedBackendId_LogsFatalWithoutThrowing()
        {
            UtfLogExpect.Error();
            // 4 号既非框架占号也在保留区内：抛出路径（Register(ISaveSerializer)）拒收，声明式路径记 Fatal 后丢弃
            Assert.DoesNotThrow(() => SaveSerializerRegistry.Register(4, typeof(CustomBackendSerializer)));
            Assert.IsFalse(SaveSerializerRegistry.TryGet(4, out _), "保留区标识不得被声明式登记收下");
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
