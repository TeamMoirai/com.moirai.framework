using System;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// 默认序列化后端解析用例：配置名→实例→该 ID 在注册表里的主一一对上，含缓存、失败只报一次与空值回退。
    /// </summary>
    /// <remarks>
    /// 配置字段走 <c>internal</c> 接缝读写，不用反射（本仓测试可见性口径）。 <br />
    /// 日志判据走 <see cref="LogUtility.onMessageLogged"/>（Handler 无关），未处理日志由 <see cref="UtfLogExpect"/> 消除。 <br />
    /// <see cref="TearDown"/> 还原配置名并注销测试 ID；解析缓存按配置名失效，故残留实例不会带给后续用例。
    /// </remarks>
    public class SaveServiceSettingsDefaultSerializerTests
    {
        /// <summary>测试专用后端 ID（框架保留区之外）。</summary>
        private const ushort TestBackend = 1100;

        /// <summary>
        /// 测试专用序列化器：不带 <c>[Serializable]</c>，证明按类型名实例化不依赖引用序列化。
        /// </summary>
        private sealed class TestBackendSerializer : ISaveSerializer
        {
            public ushort BackendId => TestBackend;

            public byte[] Serialize<T>(T data)
            {
                return Array.Empty<byte>();
            }

            public T Deserialize<T>(byte[] bytes)
            {
                return default;
            }
        }

        private string _previousTypeName;
        private int _fatalCount;

        [SetUp]
        public void SetUp()
        {
            _previousTypeName = SaveServiceSettings.Instance.m_DefaultSerializerTypeName;
            _fatalCount = 0;
            LogUtility.onMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.onMessageLogged -= CaptureLog;
            SaveServiceSettings.Instance.m_DefaultSerializerTypeName = _previousTypeName;
            SaveSerializerRegistry.Unregister(TestBackend);
        }

        private void CaptureLog(ELogLevel level, string message, Exception exception)
        {
            if (level == ELogLevel.Fatal)
            {
                _fatalCount++;
            }
        }

        [Test]
        public void DefaultBackend_RegistryServesConfiguredType_NotJustItself()
        {
            string configured = SaveServiceSettings.Instance.m_DefaultSerializerTypeName;
            Type expected = string.IsNullOrWhiteSpace(configured)
                ? typeof(JsonSaveSerializer)
                : AssemblyUtility.GetType(configured);

            Assert.IsNotNull(expected, "前置：默认后端的配置名要能解析成类型");
            ushort defaultId = SaveServiceSettings.DefaultBackend;
            Assert.IsTrue(SaveSerializerRegistry.TryGet(defaultId, out ISaveSerializer serving),
                "落盘 ID 必须在注册表有主，否则块写得出读不回");
            Assert.AreEqual(expected, serving.GetType(), "读写用的实现必须是配置指名的那个");
        }

        [Test]
        public void DefaultSerializer_ConfiguredCustomType_ResolvesOnceAndBecomesIdOwner()
        {
            SaveSerializerRegistry.Unregister(TestBackend);
            Assert.IsFalse(SaveSerializerRegistry.TryGet(TestBackend, out _), "前置：该 ID 未被登记");

            SaveServiceSettings.Instance.m_DefaultSerializerTypeName = typeof(TestBackendSerializer).FullName;

            ISaveSerializer serializer = SaveServiceSettings.DefaultSerializer;
            Assert.AreEqual(typeof(TestBackendSerializer), serializer.GetType(), "类型名应解析成该实例");
            Assert.AreSame(serializer, SaveServiceSettings.DefaultSerializer, "解析只做一次，第二次取缓存");
            Assert.AreSame(serializer, SaveSerializerRegistry.GetRequired(TestBackend),
                "配置的实例必须自己成为该 ID 的读写主");
        }

        [Test]
        public void DefaultSerializer_EmptyTypeName_FallsBackToBuiltInJson()
        {
            SaveServiceSettings.Instance.m_DefaultSerializerTypeName = string.Empty;

            Assert.AreEqual(typeof(JsonSaveSerializer), SaveServiceSettings.DefaultSerializer.GetType());
            Assert.AreEqual(SaveBackendIds.JSON, SaveServiceSettings.DefaultBackend);
            Assert.AreEqual(0, _fatalCount, "留空是出厂状态，不该报 Fatal");
        }

        [Test]
        public void DefaultSerializer_UnresolvableTypeName_FallsBackToJsonAndReportsOnce()
        {
            SaveServiceSettings.Instance.m_DefaultSerializerTypeName = "No.Such.SaveSerializer";

            UtfLogExpect.Error();
            ISaveSerializer first = SaveServiceSettings.DefaultSerializer;
            ISaveSerializer second = SaveServiceSettings.DefaultSerializer;

            Assert.AreEqual(typeof(JsonSaveSerializer), first.GetType(), "解析不出要回退内置 JSON");
            Assert.AreSame(first, second, "失败结果也要按配置名缓存，否则每存一块都新建一个实例");
            Assert.AreEqual(1, _fatalCount, "同一份错配置只报一次 Fatal");
        }
    }
}
