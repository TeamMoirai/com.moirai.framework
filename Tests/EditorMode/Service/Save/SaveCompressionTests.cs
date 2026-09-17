using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Save
{
    /// <summary>
    /// V3-P2 压缩转换链测试：GZip 往返、压+加组合、旧档（无压缩位）兼容读、
    /// 文件头 CompressionProviderId 分型（未知 ID / 标志位不一致）、注册表契约。
    /// <para>压缩注入经 internal 字段 <c>_compression</c>（测试程序集在 InternalsVisibleTo 白名单内），不触达全局配置。</para>
    /// <para>错误日志断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；
    /// DefaultLogHandler 同步链路下另补 <c>LogAssert.Expect</c> 消除 UTF 的未预期日志拦截。</para>
    /// </summary>
    public class SaveCompressionTests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        /// <summary>测试用伪造压缩提供方（仅实现接口——禁止派生 [SerializeReference] 持有的框架基类，避免污染 Inspector 下拉框）。</summary>
        private sealed class FakeCompressionProvider : ICompressionProvider
        {
            private readonly byte _id;

            internal FakeCompressionProvider(byte id)
            {
                _id = id;
            }

            public byte ProviderId => _id;

            public Stream OpenCompressStream(Stream target)
            {
                return target;
            }

            public Stream OpenDecompressStream(Stream source)
            {
                return source;
            }
        }

        private const string TestFolder = "Slots";

        private PlainSaveHandler _handler;
        private string _rootPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        private static AESEncryptedSaveHandler CreateHandler(string passphrase)
        {
            var provider = new StaticSaveKeyProvider();
            provider.SetDerivationParameters(passphrase, SaveEncryptor.DEFAULT_SALT, SaveEncryptor.DEFAULT_ITERATIONS);
            return new AESEncryptedSaveHandler { KeyProvider = provider };
        }

        [SetUp]
        public void SetUp()
        {
            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-compression-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            SaveServiceHandler.s_OverrideBasePath = null;
            try
            {
                if (Directory.Exists(_rootPath))
                {
                    Directory.Delete(_rootPath, true);
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
        /// 断言已记录包含指定片段的 Error 日志。
        /// </summary>
        private void AssertErrorLogged(string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Error && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 Error 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期（仅 DefaultLogHandler 同步链路下 UTF 可见）。
        /// </summary>
        private static void ExpectErrorLogForUtf()
        {
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
            }
        }

        /// <summary>
        /// 在 <see cref="TestFolder"/> 下解析目标存档路径。
        /// </summary>
        private SaveServiceHandler.SavePaths Paths(string fileName)
        {
            return SaveServiceHandler.ResolveSavePaths(fileName, TestFolder);
        }

        #region 提供方与注册表 [PROVIDER / REGISTRY]

        [Test]
        public void GZipProvider_RoundTrips()
        {
            byte[] raw = new byte[4096];
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = (byte)(i % 7);
            }

            byte[] packed;
            using (var output = new MemoryStream())
            {
                using (Stream gzip = GZipCompressionProvider.Shared.OpenCompressStream(output))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                packed = output.ToArray();
            }

            Assert.Less(packed.Length, raw.Length, "重复模式数据应被有效压缩");

            byte[] restored;
            using (var input = new MemoryStream(packed, writable: false))
            using (Stream gunzip = GZipCompressionProvider.Shared.OpenDecompressStream(input))
            using (var restoredOutput = new MemoryStream())
            {
                gunzip.CopyTo(restoredOutput);
                restored = restoredOutput.ToArray();
            }

            Assert.AreEqual(raw, restored, "GZip 往返应还原原始字节");
        }

        [Test]
        public void Registry_GZip_BuiltIn()
        {
            Assert.IsTrue(SaveCompressionRegistry.TryGet(GZipCompressionProvider.PROVIDER_ID, out ICompressionProvider provider));
            Assert.IsInstanceOf<GZipCompressionProvider>(provider);
        }

        [Test]
        public void Registry_DuplicateId_Throws()
        {
            Assert.Throws<GameException>(() => SaveCompressionRegistry.Register(new FakeCompressionProvider(GZipCompressionProvider.PROVIDER_ID)),
                "重复 ID 登记应 fail-fast（撞 ID = 静默写坏档）");
        }

        [Test]
        public void Registry_ReservedIdZero_Throws()
        {
            Assert.Throws<GameException>(() => SaveCompressionRegistry.Register(new FakeCompressionProvider(0)),
                "ID 0 保留为「未压缩」标记，登记应 fail-fast");
        }

        [Test]
        public void Registry_RegisterThenTryGet_Hits()
        {
            var fake = new FakeCompressionProvider(77);
            SaveCompressionRegistry.Register(fake);
            try
            {
                Assert.IsTrue(SaveCompressionRegistry.TryGet(77, out ICompressionProvider provider));
                Assert.AreSame(fake, provider);
            }
            finally
            {
                SaveCompressionRegistry.Unregister(77);
            }
        }

        #endregion

        #region 压缩管线 [COMPRESSION PIPELINE]

        [Test]
        public void Compressed_SaveBlock_RoundTrips()
        {
            _handler._compression = GZipCompressionProvider.Shared;
            var paths = Paths("slot");
            var data = new SaveData { Gold = 1234, PlayerName = new string('M', 512) };

            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, data, ESaveBackend.Json, 1, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            SaveError headerError = SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            Assert.AreEqual(SaveError.None, headerError);
            Assert.AreEqual(SaveFileHeader.FlagCompressed, header.Flags & SaveFileHeader.FlagCompressed, "压缩档应置压缩标志位");
            Assert.AreEqual(GZipCompressionProvider.PROVIDER_ID, header.CompressionProviderId, "文件头应记录压缩提供方 ID");

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(1234, loaded.Gold);
            Assert.AreEqual(new string('M', 512), loaded.PlayerName);
        }

        [Test]
        public void Compressed_RepetitiveData_SmallerFile()
        {
            var data = new SaveData { Gold = 1, PlayerName = new string('M', 4096) };
            var plainPaths = Paths("plain");
            var compressedPaths = Paths("compressed");

            _handler.SaveBlockCore(plainPaths, SaveServiceHandler.MAIN_BLOCK_KEY, data, ESaveBackend.Json, 1, CancellationToken.None);
            _handler._compression = GZipCompressionProvider.Shared;
            _handler.SaveBlockCore(compressedPaths, SaveServiceHandler.MAIN_BLOCK_KEY, data, ESaveBackend.Json, 1, CancellationToken.None);

            long plainSize = new FileInfo(plainPaths.SaveFilePath).Length;
            long compressedSize = new FileInfo(compressedPaths.SaveFilePath).Length;
            Assert.Less(compressedSize, plainSize, "高冗余数据压缩后文件应更小");
        }

        [Test]
        public void LegacyUncompressedFile_LoadsWithCompressionEnabled()
        {
            // 无压缩位旧档（P2 之前写出）在开启压缩的运行时下必须原样透传读取——魔数/flags sniff 幂等
            var paths = Paths("legacy");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 42, PlayerName = "plain" }, ESaveBackend.Json, 1, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            Assert.AreEqual(0u, header.Flags & SaveFileHeader.FlagCompressed, "旧档不应置压缩标志位");
            Assert.AreEqual(0u, header.CompressionProviderId);

            _handler._compression = GZipCompressionProvider.Shared;
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error, "未压缩旧档在开启压缩后必须可读");
            Assert.AreEqual(42, loaded.Gold);
        }

        [Test]
        public void CompressedThenEncrypted_RoundTrips()
        {
            // 压 + 加组合：压缩在加密前、解压在解密后（顺序固定）
            var handler = CreateHandler("compress-then-encrypt");
            handler._compression = GZipCompressionProvider.Shared;
            var paths = Paths("combo");
            var data = new SaveData { Gold = 7, PlayerName = new string('E', 256) };

            handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, data, ESaveBackend.Json, 1, CancellationToken.None);

            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            SaveFileHeader.Read(fileBytes, out SaveFileHeader header);
            Assert.AreEqual(SaveFileHeader.FlagCompressed, header.Flags & SaveFileHeader.FlagCompressed);

            var reader = CreateHandler("compress-then-encrypt");
            SaveError error = reader.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(7, loaded.Gold);
            Assert.AreEqual(new string('E', 256), loaded.PlayerName);
        }

        #endregion

        #region 文件头分型 [HEADER CLASSIFICATION]

        /// <summary>
        /// 覆写文件头压缩提供方 ID（offset 24-27；CRC 仅覆盖载荷，头字段改动无需重算）。
        /// </summary>
        private static void PatchCompressionProviderId(string filePath, uint providerId)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            fileBytes[24] = (byte)providerId;
            fileBytes[25] = (byte)(providerId >> 8);
            fileBytes[26] = (byte)(providerId >> 16);
            fileBytes[27] = (byte)(providerId >> 24);
            File.WriteAllBytes(filePath, fileBytes);
        }

        [Test]
        public void TryLoad_UnknownCompressionProviderId_ReturnsUnsupportedVersion()
        {
            _handler._compression = GZipCompressionProvider.Shared;
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 1, PlayerName = "x" }, ESaveBackend.Json, 1, CancellationToken.None);

            // 模拟未来运行时写出的未知压缩提供方
            PatchCompressionProviderId(paths.SaveFilePath, 200);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            AssertErrorLogged("UnsupportedVersion");
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "未知压缩提供方 ID 应拒载（未来格式保护）");
            Assert.IsNull(loaded);
        }

        [Test]
        public void TryLoad_FlagSetButIdZero_ReturnsCorrupted()
        {
            _handler._compression = GZipCompressionProvider.Shared;
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 1, PlayerName = "x" }, ESaveBackend.Json, 1, CancellationToken.None);

            PatchCompressionProviderId(paths.SaveFilePath, 0);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out _);
            AssertErrorLogged("Corrupted");
            Assert.AreEqual(SaveError.Corrupted, error, "标志位置位但 ID 为零 = 头字段不一致，应判别为损坏");
        }

        [Test]
        public void TryLoad_IdSetButFlagClear_ReturnsCorrupted()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 1, PlayerName = "x" }, ESaveBackend.Json, 1, CancellationToken.None);

            PatchCompressionProviderId(paths.SaveFilePath, 1);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out _);
            AssertErrorLogged("Corrupted");
            Assert.AreEqual(SaveError.Corrupted, error, "ID 非零但标志位未置位 = 头字段不一致，应判别为损坏");
        }

        [Test]
        public void TryLoad_CorruptedGzipPayload_ReturnsCorrupted()
        {
            _handler._compression = GZipCompressionProvider.Shared;
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 1, PlayerName = "x" }, ESaveBackend.Json, 1, CancellationToken.None);

            // 破坏压缩载荷首字节（GZip 魔数）并同步重算 CRC——CRC 自洽但解压失败
            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[SaveFileHeader.Size] ^= 0xFF;
            uint patchedCrc = Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size));
            fileBytes[20] = (byte)patchedCrc;
            fileBytes[21] = (byte)(patchedCrc >> 8);
            fileBytes[22] = (byte)(patchedCrc >> 16);
            fileBytes[23] = (byte)(patchedCrc >> 24);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out _);
            AssertErrorLogged("Corrupted");
            Assert.AreEqual(SaveError.Corrupted, error, "CRC 自洽的压缩流破坏应在解压环节归一为损坏");
        }

        #endregion
    }
}
