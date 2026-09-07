using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Save
{
    /// <summary>
    /// <see cref="JsonEncryptedSaveHandler"/> 全链路（序列化 → 加密 → 文件头 → 落盘 → 读盘 → 校验 → 解密 → 反序列化）往返测试。
    /// <para>直接经 <c>protected internal</c> 成员注入密钥与调用管线（测试程序集在 <c>InternalsVisibleTo</c> 白名单内），
    /// 不创建 [Serializable] 处理器子类、不触达 <see cref="SaveServiceSettings"/> 全局配置。</para>
    /// <para>错误日志断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（运行时激活的 UnityLoggingHandler 走异步 sink，
    /// <c>LogAssert</c> 不可见）。</para>
    /// </summary>
    public class JsonEncryptedSaveHandlerTests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        private JsonEncryptedSaveHandler _handler;
        private string _directoryPath;
        private SaveServiceHandler.SavePaths _paths;
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _handler = new JsonEncryptedSaveHandler
            {
                Key = "test-key-123"
            };

            _directoryPath = Path.Combine(Path.GetTempPath(), "moirai-save-enc-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directoryPath);
            _paths = new SaveServiceHandler.SavePaths(_directoryPath, Path.Combine(_directoryPath, "slot.sav"));

            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            try
            {
                if (Directory.Exists(_directoryPath))
                {
                    Directory.Delete(_directoryPath, true);
                }
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响测试结论
            }
        }

        private void CaptureLog(ELogLevel level, string message, Exception exception)
        {
            _capturedLogs.Add((level, message));
        }

        /// <summary>
        /// 断言已记录包含指定片段的 Error 日志（运维可见性契约）。
        /// </summary>
        private void AssertErrorLogged(string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Error && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 Error 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        [Test]
        public void RoundTrip_PreservesData()
        {
            var data = new SaveData { Gold = 1234, PlayerName = "Moirai" };

            _handler.SaveCore(_paths, data, CancellationToken.None);

            Assert.IsTrue(File.Exists(_paths.SaveFilePath), "序列化后应落盘");
            Assert.Greater(new FileInfo(_paths.SaveFilePath).Length, SaveFileHeader.Size + 16 + 32, "文件应包含文件头与完整密文");

            SaveError error = _handler.TryLoadCore<SaveData>(_paths, out SaveData loaded);

            Assert.AreEqual(SaveError.None, error, "往返加载应成功");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1234, loaded.Gold);
            Assert.AreEqual("Moirai", loaded.PlayerName);
        }

        [Test]
        public void WrongKey_FailsAtIntegrityCheck()
        {
            var writer = new JsonEncryptedSaveHandler { Key = "key-for-write" };
            var reader = new JsonEncryptedSaveHandler { Key = "key-for-read" };

            writer.SaveCore(_paths, new SaveData { Gold = 99, PlayerName = "Moirai" }, CancellationToken.None);

            // 错误密钥在 HMAC 层被拦截（encrypt-then-MAC）——判别为完整性失败而非解密失败
            SaveError error = reader.TryLoadCore<SaveData>(_paths, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.IntegrityCheckFailed, error, "密钥不符应在 HMAC 层被拦截");
            Assert.IsNull(loaded, "密钥不符应返回默认值，而非抛出或返回垃圾数据");
        }

        [Test]
        public void TamperedFile_FailsAtIntegrityCheck()
        {
            _handler.SaveCore(_paths, new SaveData { Gold = 1, PlayerName = "Moirai" }, CancellationToken.None);

            // 攻击者模型：翻转密文字节后同步修正文件头 CRC，使存储校验通过——HMAC 层仍必须拦截
            byte[] fileBytes = File.ReadAllBytes(_paths.SaveFilePath);
            fileBytes[^1] ^= 0xFF;
            uint patchedCrc = Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size));
            fileBytes[20] = (byte)patchedCrc;
            fileBytes[21] = (byte)(patchedCrc >> 8);
            fileBytes[22] = (byte)(patchedCrc >> 16);
            fileBytes[23] = (byte)(patchedCrc >> 24);
            File.WriteAllBytes(_paths.SaveFilePath, fileBytes);

            SaveError error = _handler.TryLoadCore<SaveData>(_paths, out SaveData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.IntegrityCheckFailed, error, "CRC 自洽的密文篡改应被 HMAC 拦截");
            Assert.IsNull(loaded);
        }

        [Test]
        public void Serialize_IsDeterministicLayout()
        {
            // 两次序列化布局稳定（IV 随机导致密文不同，但长度结构一致：IV + 填充块 + MAC）
            var data = new SaveData { Gold = 7, PlayerName = "Moirai" };

            byte[] first = _handler.Serialize(data);
            byte[] second = _handler.Serialize(data);

            Assert.AreEqual(first.Length, second.Length, "相同明文的密文长度结构应一致");
            CollectionAssert.AreNotEqual(first, second, "随机 IV 应使两次密文不同");
        }
    }
}
