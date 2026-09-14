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
    /// V3-P2 密钥提供方测试：静态密钥（V2 语义等价）、口令注入、HKDF 按用户派生，
    /// 以及加密处理器经 <see cref="ISaveKeyProvider"/> 密钥来源的全链路往返。
    /// <para>处理器密钥提供方注入经 internal 字段 <c>_keyProvider</c>（测试程序集在 InternalsVisibleTo 白名单内）；
    /// 测试不提供方派生 [SerializeReference] 持有的框架基类（避免污染 Inspector 下拉框）——全部使用框架内置实现。</para>
    /// <para>错误日志断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；
    /// DefaultLogHandler 同步链路下另补 <c>LogAssert.Expect</c> 消除 UTF 的未预期日志拦截。</para>
    /// </summary>
    public class SaveKeyProviderTests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        private string _rootPath;
        private string _directoryPath;
        private SaveServiceHandler.SavePaths _paths;
        private List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-key-tests-" + Guid.NewGuid().ToString("N"));
            _directoryPath = Path.Combine(_rootPath, SaveServiceHandler.DATA_FOLDER_NAME, "Slots");
            Directory.CreateDirectory(_directoryPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;
            _paths = new SaveServiceHandler.SavePaths(_directoryPath, Path.Combine(_directoryPath, "slot.sav"));

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

        #region 静态密钥 [STATIC KEY]

        [Test]
        public void Static_Default_MatchesEncryptorPlaceholderDerivation()
        {
            // 默认静态提供方 = PBKDF2(占位口令, 默认盐, 默认迭代)
            SaveError error = StaticSaveKeyProvider.Default.TryGetKeyMaterial(out byte[] encKey, out byte[] macKey);
            Assert.AreEqual(SaveError.None, error);

            byte[] expected = SaveEncryptor.DeriveKeyMaterial(SaveEncryptor.DEFAULT_PASSPHRASE, SaveEncryptor.DEFAULT_SALT, SaveEncryptor.DEFAULT_ITERATIONS);
            Assert.AreEqual(32, encKey.Length);
            Assert.AreEqual(32, macKey.Length);
            Assert.AreEqual(new Span<byte>(expected, 0, 32).ToArray(), encKey, "加密密钥应与占位派生一致");
            Assert.AreEqual(new Span<byte>(expected, 32, 32).ToArray(), macKey, "认证密钥应与占位派生一致");
        }

        [Test]
        public void Static_CachesDerivedMaterial()
        {
            var provider = new StaticSaveKeyProvider();
            provider.TryGetKeyMaterial(out byte[] encKey1, out byte[] macKey1);
            provider.TryGetKeyMaterial(out byte[] encKey2, out byte[] macKey2);

            Assert.AreSame(encKey1, encKey2, "同参数应命中缓存（PBKDF2 为 10 万迭代级开销）");
            Assert.AreSame(macKey1, macKey2);
        }

        [Test]
        public void Static_Configure_InvalidatesCache()
        {
            var provider = new StaticSaveKeyProvider();
            provider.TryGetKeyMaterial(out byte[] before, out _);

            provider.Configure("new-project-secret", SaveEncryptor.DEFAULT_SALT, SaveEncryptor.DEFAULT_ITERATIONS);
            provider.TryGetKeyMaterial(out byte[] after, out _);

            Assert.AreNotSame(before, after, "参数变更应重派生");
            CollectionAssert.AreNotEqual(before, after, "不同口令应产出不同密钥");
        }

        [Test]
        public void KeyBridge_ThenStaticProvider_CrossReads()
        {
            // Key 属性桥接（V2 契约）与静态提供方同参派生等价——两种注入方式的档互读
            var writer = new AESEncryptedSaveHandler { Key = "cross-key" };
            writer.SaveBlockCore(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 88, PlayerName = "bridge" }, ESaveBackend.Json, 1, CancellationToken.None);

            var staticProvider = new StaticSaveKeyProvider();
            staticProvider.Configure("cross-key", SaveEncryptor.DEFAULT_SALT, SaveEncryptor.DEFAULT_ITERATIONS);
            var reader = new AESEncryptedSaveHandler { _keyProvider = staticProvider };

            SaveError error = reader.TryLoadBlockCore<SaveData>(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error, "Key 桥接与静态提供方同参派生应互读");
            Assert.AreEqual(88, loaded.Gold);
        }

        [Test]
        public void KeyBridge_Getter_ReflectsConfiguredPassphrase()
        {
            var handler = new AESEncryptedSaveHandler();
            Assert.AreEqual(SaveEncryptor.DEFAULT_PASSPHRASE, handler.Key, "未设置时应回显占位默认值");

            handler.Key = "explicit-key";
            Assert.AreEqual("explicit-key", handler.Key);
        }

        #endregion

        #region 口令注入 [PASSPHRASE]

        [Test]
        public void Passphrase_NotSet_ReturnsInvalidArgument()
        {
            var provider = new PassphraseSaveKeyProvider();
            Assert.IsFalse(provider.HasPassphrase);

            SaveError error = provider.TryGetKeyMaterial(out byte[] encKey, out byte[] macKey);
            Assert.AreEqual(SaveError.InvalidArgument, error, "未注入口令应分型为参数错误");
            Assert.IsNull(encKey);
            Assert.IsNull(macKey);
        }

        [Test]
        public void Passphrase_SetThenClear_Transitions()
        {
            var provider = new PassphraseSaveKeyProvider();
            provider.SetPassphrase("player-password");
            Assert.IsTrue(provider.HasPassphrase);

            SaveError error = provider.TryGetKeyMaterial(out byte[] encKey, out _);
            Assert.AreEqual(SaveError.None, error);
            Assert.IsNotNull(encKey);

            provider.ClearPassphrase();
            Assert.IsFalse(provider.HasPassphrase);
            Assert.AreEqual(SaveError.InvalidArgument, provider.TryGetKeyMaterial(out _, out _), "清除口令后应回到未注入分型");
        }

        [Test]
        public void Passphrase_HandlerRoundTrip()
        {
            var writerProvider = new PassphraseSaveKeyProvider();
            writerProvider.SetPassphrase("player-password");
            var writer = new AESEncryptedSaveHandler { _keyProvider = writerProvider };
            writer.SaveBlockCore(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 66, PlayerName = "locked" }, ESaveBackend.Json, 1, CancellationToken.None);

            var readerProvider = new PassphraseSaveKeyProvider();
            readerProvider.SetPassphrase("player-password");
            var reader = new AESEncryptedSaveHandler { _keyProvider = readerProvider };

            SaveError error = reader.TryLoadBlockCore<SaveData>(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(66, loaded.Gold);
        }

        [Test]
        public void Passphrase_HandlerLoad_Unset_ReturnsInvalidArgument()
        {
            var writer = new AESEncryptedSaveHandler { Key = "any" };
            writer.SaveBlockCore(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 1, PlayerName = "x" }, ESaveBackend.Json, 1, CancellationToken.None);

            var reader = new AESEncryptedSaveHandler { _keyProvider = new PassphraseSaveKeyProvider() };

            ExpectErrorLogForUtf();
            SaveError error = reader.TryLoadBlockCore<SaveData>(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            AssertErrorLogged("InvalidArgument");
            Assert.AreEqual(SaveError.InvalidArgument, error, "未注入口令的读取应分型为参数错误");
            Assert.IsNull(loaded);
        }

        [Test]
        public void Passphrase_HandlerSave_Unset_ThrowsGameException()
        {
            // 写路径 fail-fast 契约：密钥材料不可得 = 写入失败抛 GameException
            var writer = new AESEncryptedSaveHandler { _keyProvider = new PassphraseSaveKeyProvider() };
            Assert.Throws<GameException>(() =>
                writer.SaveBlockCore(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 1, PlayerName = "x" }, ESaveBackend.Json, 1, CancellationToken.None));
        }

        #endregion

        #region HKDF 按用户派生 [HKDF PER-USER]

        [Test]
        public void Hkdf_PerUser_ProducesDistinctKeys()
        {
            var providerA = new HKDFPerUserSaveKeyProvider { UserId = "user-a" };
            var providerB = new HKDFPerUserSaveKeyProvider { UserId = "user-b" };

            providerA.TryGetKeyMaterial(out byte[] encKeyA, out byte[] macKeyA);
            providerB.TryGetKeyMaterial(out byte[] encKeyB, out byte[] macKeyB);

            CollectionAssert.AreNotEqual(encKeyA, encKeyB, "不同用户应产出独立加密密钥");
            CollectionAssert.AreNotEqual(macKeyA, macKeyB, "不同用户应产出独立认证密钥");
        }

        [Test]
        public void Hkdf_SameUser_Deterministic()
        {
            var first = new HKDFPerUserSaveKeyProvider { UserId = "user-a" };
            var second = new HKDFPerUserSaveKeyProvider { UserId = "user-a" };

            first.TryGetKeyMaterial(out byte[] encKey1, out _);
            second.TryGetKeyMaterial(out byte[] encKey2, out _);

            CollectionAssert.AreEqual(encKey1, encKey2, "HKDF 同参派生必须确定（跨会话读档前提）");
        }

        [Test]
        public void Hkdf_NoUser_DerivesDefaultSlot()
        {
            var provider = new HKDFPerUserSaveKeyProvider();
            SaveError error = provider.TryGetKeyMaterial(out byte[] encKey, out byte[] macKey);

            Assert.AreEqual(SaveError.None, error, "未设用户 ID 应以空盐派生默认档");
            Assert.IsNotNull(encKey);
            Assert.IsNotNull(macKey);
        }

        [Test]
        public void Hkdf_HandlerIsolation_PerUser()
        {
            var writer = new AESEncryptedSaveHandler
            {
                _keyProvider = new HKDFPerUserSaveKeyProvider { UserId = "user-a" }
            };
            writer.SaveBlockCore(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 42, PlayerName = "user-a-data" }, ESaveBackend.Json, 1, CancellationToken.None);

            var wrongUser = new AESEncryptedSaveHandler
            {
                _keyProvider = new HKDFPerUserSaveKeyProvider { UserId = "user-b" }
            };

            ExpectErrorLogForUtf();
            SaveError wrongError = wrongUser.TryLoadBlockCore<SaveData>(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData rejected);
            AssertErrorLogged("IntegrityCheckFailed");
            Assert.AreEqual(SaveError.IntegrityCheckFailed, wrongError, "他用户密钥应在 HMAC 层被拦截");
            Assert.IsNull(rejected);

            var sameUser = new AESEncryptedSaveHandler
            {
                _keyProvider = new HKDFPerUserSaveKeyProvider { UserId = "user-a" }
            };
            SaveError error = sameUser.TryLoadBlockCore<SaveData>(_paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error, "同用户应可读回");
            Assert.AreEqual(42, loaded.Gold);
        }

        #endregion
    }
}
