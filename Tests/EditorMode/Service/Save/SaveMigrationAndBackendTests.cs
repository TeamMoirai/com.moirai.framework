using System;
using static Moirai.Atropos.LogHandler;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// 序列化后端（框架内置 JSON）与 <see cref="SaveDataBlock"/> 版本迁移管线测试。
    /// </summary>
    /// <remarks>
    /// 数据类建在测试程序集内（<see cref="SaveDataBlock"/> 非 <c>[SerializeReference]</c> 持有类型，无 Inspector 污染）； <br />
    /// 迁移/未来版本保护经 internal 管线直调（<c>InternalsVisibleTo</c>），真实文件 IO。 <br />
    /// MessagePack / MemoryPack / protobuf-net 三个第三方后端已外置到 plugin 分支发行仓，其标注类型与对应用例随各插件自带
    /// （<c>&lt;id&gt;/Tests/</c>）；本夹具只留契约层用例：注册表、内置 JSON 往返与迁移管线。
    /// </remarks>
    public partial class SaveMigrationAndBackendTests
    {
        /// <summary>
        /// 迁移测试用数据：v1 只有两字段，v3 当前形态——OnMigrate 级联回填。
        /// </summary>
        [SaveData("migrating", 3)]
        private sealed class MigratingData : SaveDataBlock
        {
            public string Nickname;
            public int Score;
            public bool MigratedFromV1;
            public bool MigratedFromV2;

            protected internal override void OnMigrate(int fromVersion)
            {
                switch (fromVersion)
                {
                    case 1:
                        Nickname = "wanderer-" + Score;
                        MigratedFromV1 = true;
                        goto case 2;
                    case 2:
                        MigratedFromV2 = true;
                        break;
                }
            }
        }

        /// <summary>
        /// 缺 <see cref="SaveDataAttribute"/> 的非法 SaveDataBlock 子类（契约破坏 fail-fast 用）。
        /// </summary>
        private sealed class AttributeLessData : SaveDataBlock
        {
            public int X;
        }

        /// <summary>
        /// 无基类普通 POCO（版本字段不参与迁移语义）。
        /// </summary>
        [Serializable]
        private sealed class PlainBox
        {
            public int X;
        }

        private PlainSaveHandler _handler;
        private string _rootPath;
        private System.Collections.Generic.List<(ELogLevel Level, string Message)> _capturedLogs;

        [SetUp]
        public void SetUp()
        {
            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-migration-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _capturedLogs = new System.Collections.Generic.List<(ELogLevel, string)>();
            LogUtility.onMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.onMessageLogged -= CaptureLog;
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
        /// 断言已记录包含指定片段的 Error 日志（运维可见性契约）。
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
            UtfLogExpect.Error();
        }

        private SaveServiceHandler.SavePaths Paths(string fileName)
        {
            return SaveServiceHandler.ResolveSavePaths(fileName, "Slots");
        }

        #region 版本迁移 [MIGRATION]

        [Test]
        public void Migration_FromV1_CascadesToCurrent()
        {
            // 旧档模拟：以 v1 版本号写入当前形态数据（旧档字段是旧集合的子集，JSON 兼容解析），
            // 加载时 OnMigrate(1) 级联执行并就地回填
            var paths = Paths("slot");
            var oldShape = new MigratingData { Score = 42 };
            _handler.SaveBlockCore(paths, "migrating", oldShape, SaveBackendIds.JSON, 1, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<MigratingData>(paths, "migrating", out MigratingData loaded);

            Assert.AreEqual(SaveError.None, error);
            Assert.IsTrue(loaded.MigratedFromV1, "v1→v2 级联应执行");
            Assert.IsTrue(loaded.MigratedFromV2, "v2→v3 级联应执行");
            Assert.AreEqual("wanderer-42", loaded.Nickname, "v1 缺失字段应由迁移回填");
            Assert.AreEqual(42, loaded.Score, "既有字段不应被迁移破坏");
        }

        [Test]
        public void Migration_CurrentVersion_SkipsOnMigrate()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "migrating", new MigratingData { Score = 7 }, SaveBackendIds.JSON, 3, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<MigratingData>(paths, "migrating", out MigratingData loaded);

            Assert.AreEqual(SaveError.None, error);
            Assert.IsFalse(loaded.MigratedFromV1, "版本一致不应触发迁移");
        }

        [Test]
        public void Migration_FutureVersion_ReturnsUnsupportedVersion()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "migrating", new MigratingData { Score = 7 }, SaveBackendIds.JSON, 99, CancellationToken.None);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<MigratingData>(paths, "migrating", out MigratingData loaded);

            AssertErrorLogged("Load failed");
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "高于声明版本的存档应拒绝加载（未来格式保护）");
            Assert.IsNull(loaded);
        }

        [Test]
        public void Migration_MissingAttribute_ReturnsSerializationFailed()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "bare", new AttributeLessData { X = 1 }, SaveBackendIds.JSON, 1, CancellationToken.None);

            ExpectErrorLogForUtf();

            SaveError error = _handler.TryLoadBlockCore<AttributeLessData>(paths, "bare", out AttributeLessData loaded);

            AssertErrorLogged("missing SaveDataAttribute");
            Assert.AreEqual(SaveError.SerializationFailed, error, "SaveDataBlock 子类缺声明应 fail-fast");
            Assert.IsNull(loaded);
        }

        [Test]
        public void Migration_PlainPoco_NoVersionEnforcement()
        {
            // 非 SaveDataBlock 类型不参与迁移语义：任意版本号均可加载（JSON 字段级兼容）
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "plain", new PlainBox { X = 5 }, SaveBackendIds.JSON, 42, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<PlainBox>(paths, "plain", out PlainBox loaded);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(5, loaded.X);
        }

        #endregion

        #region 序列化后端 [BACKENDS]

        [Test]
        public void Registry_ProvidesBuiltInJsonBackend()
        {
            Assert.IsTrue(SaveSerializerRegistry.TryGet(SaveBackendIds.JSON, out _));
        }

        [Test]
        public void Registry_UnknownBackend_GetRequired_FailFast()
        {
            // 未注册后端（如未接入的 KeyValue）必须 fail-fast 而非静默降级
            Assert.Throws<GameException>(() => SaveSerializerRegistry.GetRequired(999));
        }

        [Test]
        public void JsonBackend_InFileRoundTrips()
        {
            var paths = Paths("slot");
            _handler.SaveBlockCore(paths, "json-block", new PlainBox { X = 1 }, SaveBackendIds.JSON, 1, CancellationToken.None);

            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<PlainBox>(paths, "json-block", out PlainBox a));
            Assert.AreEqual(1, a.X);
        }

        #endregion

        #region 描述符 [DESCRIPTOR]

        /// <summary>
        /// 经生成的 <c>Internal_UseHandler</c> 直设外观处理器（Handler 属性 setter 拒绝 null）。
        /// </summary>
        private static void SetHandler(SaveServiceHandler value) => SaveService.Internal_UseHandler(value);

        [Test]
        public void Descriptor_AttributeOverridesSettingsBackend()
        {
            // [SaveData(Backend=...)] 显式声明优先于设置默认后端（经外观写路径验证）
            SetHandler(_handler);
            try
            {
                SaveService.SaveBlock(new MigratingData { Score = 9 }, "slot", "desc", "Slots");
                SaveBlockInfo[] infos = SaveService.GetBlockInfos("slot", "Slots");
                Assert.AreEqual(SaveBackendIds.JSON, infos[0].Backend, "MigratingData 声明 Backend=Json，应覆盖设置默认值");
            }
            finally
            {
                SetHandler(null);
            }
        }

        #endregion
    }
}
