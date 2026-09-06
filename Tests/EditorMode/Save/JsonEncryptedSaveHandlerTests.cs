using System;
using System.IO;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Save
{
    /// <summary>
    /// <see cref="JsonEncryptedSaveHandler"/> 全链路（序列化 → 加密 → 落盘 → 读盘 → 解密 → 反序列化）往返测试。
    /// <para>经测试派生类暴露 <c>protected</c> 密钥注入与异步 API，不触达 <see cref="SaveServiceSettings"/> 全局配置。</para>
    /// </summary>
    public class JsonEncryptedSaveHandlerTests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        private sealed class TestHandler : JsonEncryptedSaveHandler
        {
            public void Configure(string key) => Key = key;

            public UniTask SerializeTo(object obj, FileStream stream) => SerializeAsync(obj, stream);

            public UniTask<T> LoadFrom<T>(FileStream stream) => DeserializeAsync<T>(stream);
        }

        private string _filePath;

        [SetUp]
        public void SetUp()
        {
            _filePath = Path.Combine(Path.GetTempPath(), "moirai-save-test-" + Guid.NewGuid().ToString("N") + ".sav");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }

        [Test]
        public void RoundTrip_PreservesData()
        {
            var handler = new TestHandler();
            handler.Configure("test-key-123");

            var data = new SaveData { Gold = 1234, PlayerName = "Moirai" };

            using (var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write))
            {
                handler.SerializeTo(data, stream).GetAwaiter().GetResult();
            }

            Assert.IsTrue(File.Exists(_filePath), "序列化后应落盘");
            Assert.Greater(new FileInfo(_filePath).Length, 16, "密文应至少包含一个 AES 块（非明文 JSON）");

            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
            {
                var loaded = handler.LoadFrom<SaveData>(stream).GetAwaiter().GetResult();

                Assert.IsNotNull(loaded);
                Assert.AreEqual(1234, loaded.Gold);
                Assert.AreEqual("Moirai", loaded.PlayerName);
            }
        }

        [Test]
        public void WrongKey_LoadReturnsDefault()
        {
            var writer = new TestHandler();
            writer.Configure("key-for-write");
            var reader = new TestHandler();
            reader.Configure("key-for-read");

            var data = new SaveData { Gold = 99, PlayerName = "Moirai" };
            using (var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write))
            {
                writer.SerializeTo(data, stream).GetAwaiter().GetResult();
            }

            // 损坏兜底路径会记录 Error 日志（运维可见性契约）——显式声明预期，避免被测试运行器判为未预期错误日志
            LogAssert.Expect(LogType.Error, new Regex("Decryption failed"));

            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
            {
                // EncryptedSaveHandlerBase 捕获 CryptographicException——损坏/密钥不符应返回默认值（损坏兜底契约）
                var loaded = reader.LoadFrom<SaveData>(stream).GetAwaiter().GetResult();
                Assert.IsNull(loaded, "密钥不符应走损坏兜底返回 null，而非抛出或返回垃圾数据");
            }
        }
    }
}
