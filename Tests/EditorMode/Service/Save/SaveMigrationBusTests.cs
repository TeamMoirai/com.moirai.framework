using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// V3-P5 迁移总线测试：版本链解析（单步/多步/缺链/歧义/降级拒绝/注册校验）、迁移上下文块级与字段级操作（JSON/KVT）、
    /// 回写开/关行为、迁移历史审计、元数据版本盖章、显式 MigrateSave、组件模式版本钩子路由。
    /// <para>全部经 internal 同步核心路径（真实文件 IO）；迁移器类为顶层 internal（SaveHost SG 自注册可发现），
    /// SetUp 手动注册兜底（SG 对测试程序集生成不稳定）——TearDown 经 <c>SaveMigrationManager.ResetForTests</c> 复位全局状态防串扰。</para>
    /// <para>错误日志断言经 <see cref="LogUtility.OnMessageLogged"/> 捕获；DefaultLogHandler 同步链路下补 <c>LogAssert.Expect</c>。</para>
    /// </summary>
    public class SaveMigrationBusTests
    {
        #region 测试数据与迁移器 [FIXTURES]

        /// <summary>JSON 块测试数据（v1 形态）。</summary>
        [Serializable]
        private sealed class ProfileData
        {
            public int Gold;
            public string Title;
        }

        /// <summary>v2 形态（Gold 改名 Coins）。</summary>
        [Serializable]
        private sealed class ProfileDataV2
        {
            public int Coins;
            public string Title;
        }

        /// <summary>v3 形态（Coins 改型 string）。</summary>
        [Serializable]
        private sealed class ProfileDataV3
        {
            public string Coins;
            public string Title;
        }

        /// <summary>声明块键的测试数据（RenameField&lt;T&gt; 键解析用）。</summary>
        [SaveData("hero", 1)]
        [Serializable]
        private sealed class HeroData
        {
            public int Level;
        }

        /// <summary>HeroData 改名后形态（Level → level）。</summary>
        [Serializable]
        private sealed class HeroDataV2
        {
            public int level;
        }

        /// <summary>迁移记录探针基类（记录执行轨迹）。</summary>
        internal abstract class ProbeMigratorBase : ISaveMigrator
        {
            public abstract int FromVersion { get; }
            public abstract int ToVersion { get; }
            public virtual int Priority => 0;
            public abstract UniTask Migrate(SaveMigrationContext context);
        }

        /// <summary>1→2：ProfileData 块字段 Gold → Coins 改名。</summary>
        internal sealed class RenameGoldMigrator : ProbeMigratorBase
        {
            public static int RunCount;
            public override int FromVersion => 1;
            public override int ToVersion => 2;

            public override UniTask Migrate(SaveMigrationContext context)
            {
                RunCount++;
                context.RenameField("profile", "Gold", "Coins");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>2→3：ProfileData 块 Coins int → string 改型。</summary>
        internal sealed class RetypeCoinsMigrator : ProbeMigratorBase
        {
            public static int RunCount;
            public override int FromVersion => 2;
            public override int ToVersion => 3;

            public override UniTask Migrate(SaveMigrationContext context)
            {
                RunCount++;
                context.RetypeField<int, string>("profile", "Coins", coins => "coins-" + coins);
                return UniTask.CompletedTask;
            }
        }

        /// <summary>同边优先级探针 A（Priority 10，应当后执行）。</summary>
        internal sealed class PriorityLateMigrator : ProbeMigratorBase
        {
            public static readonly List<string> ExecutionOrder = new List<string>();
            public override int FromVersion => 5;
            public override int ToVersion => 6;
            public override int Priority => 10;

            public override UniTask Migrate(SaveMigrationContext context)
            {
                ExecutionOrder.Add("late");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>同边优先级探针 B（Priority 1，应当先执行）。</summary>
        internal sealed class PriorityEarlyMigrator : ProbeMigratorBase
        {
            public override int FromVersion => 5;
            public override int ToVersion => 6;
            public override int Priority => 1;

            public override UniTask Migrate(SaveMigrationContext context)
            {
                PriorityLateMigrator.ExecutionOrder.Add("early");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>抛异常迁移器（fail-fast 归一用）。</summary>
        internal sealed class ThrowingMigrator : ProbeMigratorBase
        {
            public override int FromVersion => 1;
            public override int ToVersion => 2;

            public override UniTask Migrate(SaveMigrationContext context)
            {
                throw new InvalidOperationException("boom");
            }
        }

        /// <summary>返回未完成任务的迁移器（同步完成断言用）。</summary>
        internal sealed class AsyncViolatingMigrator : ProbeMigratorBase
        {
            public override int FromVersion => 1;
            public override int ToVersion => 2;

            public override UniTask Migrate(SaveMigrationContext context)
            {
                // 永不完成的任务（无 PlayerLoop 依赖，纯对象）
                return new UniTaskCompletionSource().Task;
            }
        }

        /// <summary>非法版本边迁移器（To &lt;= From）。</summary>
        /// <remarks>必须用 struct——生成器只扫描 class 声明，避免非法迁移器被自注册进模块初始化器（注册期 ArgumentException 会炸毁整个程序集的模块初始化）。</remarks>
        private struct InvalidEdgeMigrator : ISaveMigrator
        {
            public int FromVersion => 2;
            public int ToVersion => 2;
            public int Priority => 0;

            public UniTask Migrate(SaveMigrationContext context) => UniTask.CompletedTask;
        }

        /// <summary>TransformBlock 探针（1→2 整块变换）。</summary>
        internal sealed class TransformBlockMigrator : ProbeMigratorBase
        {
            public override int FromVersion => 1;
            public override int ToVersion => 2;

            public override UniTask Migrate(SaveMigrationContext context)
            {
                context.TransformBlock<ProfileData>("profile", old => new ProfileData { Gold = old.Gold * 10, Title = old.Title }, newDataVersion: 1);
                return UniTask.CompletedTask;
            }
        }

        /// <summary>含模式版本迁移钩子的测试组件。</summary>
        private sealed class SchemaProbeComponent : MonoBehaviour, ISaveComponentMigrator
        {
            internal int Value;
            internal int MigratedFrom = -1;
            internal string MigratedText;

            /// <summary>迁移钩子：v1 存档中 Value 键为 legacy_value（字符串），改为 int Value。</summary>
            public void OnMigrateComponent(int fromVersion, ref SaveKeyValueReader reader, int recordCount)
            {
                MigratedFrom = fromVersion;
                for (int i = 0; i < recordCount; i++)
                {
                    if (!reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
                    {
                        return;
                    }

                    string keyText = System.Text.Encoding.UTF8.GetString(key);
                    if (keyText == "legacy_value" && type == ESaveKvType.String)
                    {
                        MigratedText = reader.ReadString();
                    }
                    else
                    {
                        reader.SkipRecordPayload();
                    }
                }
            }
        }

        /// <summary>SchemaProbeComponent 的手写捕获器（当前模式版本 2；SG 对测试程序集生成不稳定的兜底惯例）。</summary>
        private sealed class SchemaProbeFallbackCapturer : ISaveComponentCapturer
        {
            private static readonly string[] s_FieldNames = { "Value" };

            public Type ComponentType => typeof(SchemaProbeComponent);
            public string[] FieldNames => s_FieldNames;
            public int SchemaVersion => 2;

            public void Capture(object component, ref SaveKeyValueWriter writer, in SaveFieldMask mask)
            {
                // 经 SaveComponent 驱动的捕获器必须写「类型全名」嵌套作用域（SG 生成契约——SaveComponent.Restore 按作用域路由）
                var self = (SchemaProbeComponent)component;
                writer.BeginNestedObject(ComponentType.FullName, mask.IsEnabled(0) ? 1 : 0);
                if (mask.IsEnabled(0))
                {
                    writer.WriteInt32("Value", self.Value);
                }

                writer.EndNested();
            }

            public void Restore(object component, ref SaveKeyValueReader reader, int recordCount, in SaveFieldMask mask)
            {
                var self = (SchemaProbeComponent)component;
                for (int i = 0; i < recordCount; i++)
                {
                    if (!reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
                    {
                        return;
                    }

                    string keyText = System.Text.Encoding.UTF8.GetString(key);
                    if (keyText == "Value" && type == ESaveKvType.Int32 && mask.IsEnabled(0))
                    {
                        self.Value = reader.ReadInt32();
                    }
                    else
                    {
                        reader.SkipRecordPayload();
                    }
                }
            }
        }

        #endregion

        private const string TestFolder = "Slots";

        private PlainSaveHandler _handler;
        private string _rootPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;
        private List<SaveFailedArgs> _loadFailedEvents;
        private List<SaveFailedArgs> _saveFailedEvents;

        [SetUp]
        public void SetUp()
        {
            // 先复位迁移全局状态（SG 可能自注册了测试迁移器；防跨夹具串扰）
            SaveMigrationManager.ResetForTests();
            RenameGoldMigrator.RunCount = 0;
            RetypeCoinsMigrator.RunCount = 0;
            PriorityLateMigrator.ExecutionOrder.Clear();

            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-bus-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;
            _loadFailedEvents = new List<SaveFailedArgs>();
            _saveFailedEvents = new List<SaveFailedArgs>();
            SaveService.LoadFailed += OnLoadFailed;
            SaveService.SaveFailed += OnSaveFailed;
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.LoadFailed -= OnLoadFailed;
            SaveService.SaveFailed -= OnSaveFailed;
            LogUtility.OnMessageLogged -= CaptureLog;
            SaveMigrationManager.ResetForTests();
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

        private void OnLoadFailed(SaveFailedArgs args) => _loadFailedEvents.Add(args);
        private void OnSaveFailed(SaveFailedArgs args) => _saveFailedEvents.Add(args);

        private static void ExpectErrorLogForUtf()
        {
            UtfLogExpect.Error();
        }

        private static void ExpectWarningLogForUtf()
        {
            UtfLogExpect.Warning();
        }

        private void AssertErrorLogged(string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == ELogLevel.Error && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 Error 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        private SaveServiceHandler.SavePaths Paths(string fileName)
        {
            return SaveServiceHandler.ResolveSavePaths(fileName, TestFolder);
        }

        /// <summary>
        /// 写入指定数据版本的存档（版本化激活下盖章 __meta）。
        /// </summary>
        private void WriteSlotAtVersion(string fileName, ProfileData data, int fileVersion)
        {
            SaveMigrationManager.CurrentVersion = fileVersion;
            _handler.SaveBlockCore(Paths(fileName), "profile", data, ESaveBackend.Json, 1, CancellationToken.None);
        }

        /// <summary>
        /// 读取槽位元数据（内部键直读）。
        /// </summary>
        private SaveMetadata ReadMeta(string fileName)
        {
            SaveError error = _handler.TryLoadBlockCore<SaveMetadata>(Paths(fileName), SaveServiceHandler.META_BLOCK_KEY, out SaveMetadata metadata);
            Assert.AreEqual(SaveError.None, error, "元数据块应可读");
            return metadata;
        }

        #region 版本链 [CHAIN]

        [Test]
        public void SessionCache_ExternalRewrite_PassivelyInvalidates()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 42, Title = "knight" }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            // 首次加载迁移并标记会话缓存（回写后文件时间变化 → 二次加载经版本相等短路重锚）
            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out _));
            Assert.AreEqual(1, RenameGoldMigrator.RunCount);

            // 同会话加载：版本相等短路（不重复执行迁移器）
            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out _));
            Assert.AreEqual(1, RenameGoldMigrator.RunCount, "同会话不得重复迁移");

            // 外部把旧版内容重写回存档（模拟云同步落盘/外部替换）——写入时间变化必须使缓存被动失效
            WriteSlotAtVersion("slot", new ProfileData { Gold = 7, Title = "squire" }, 1);
            File.SetLastWriteTimeUtc(Paths("slot").SaveFilePath, DateTime.UtcNow.AddSeconds(2));
            SaveMigrationManager.CurrentVersion = 2;

            Assert.AreEqual(SaveError.None, _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out ProfileDataV2 reloaded));
            Assert.AreEqual(2, RenameGoldMigrator.RunCount, "外部替换改变写入时间必须被动失效会话缓存并重迁移");
            Assert.AreEqual(7, reloaded.Coins, "重写后的旧版数据应重新迁移");
            Assert.AreEqual("squire", reloaded.Title);
        }

        [Test]
        public void Chain_SingleStep_MigratesAndWritesBack()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 42, Title = "knight" }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            SaveError error = _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out ProfileDataV2 loaded);

            // 迁移后 JSON 键已从 Gold 改为 Coins——按新形态类型化读出校验
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(42, loaded.Coins, "改名后的字段应可读回");
            Assert.AreEqual("knight", loaded.Title);
            Assert.AreEqual(1, RenameGoldMigrator.RunCount, "迁移器应恰好执行一次");

            // 回写持久化：元数据版本推进 + 审计历史落盘
            SaveMetadata metadata = ReadMeta("slot");
            Assert.AreEqual(2, metadata.SaveVersion, "回写后元数据版本应为当前版本");
            Assert.NotNull(metadata.MigrationHistory, "迁移历史应已写入");
            Assert.IsTrue(metadata.MigrationHistory.Exists(entry => entry.StartsWith("1->2|", StringComparison.Ordinal)), "历史应记录 1->2 迁移步");

            // 再次加载（同版本短路）不重复执行迁移器
            SaveError secondError = _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out _);
            Assert.AreEqual(SaveError.None, secondError);
            Assert.AreEqual(1, RenameGoldMigrator.RunCount, "回写后重载不应重跑迁移链");
        }

        [Test]
        public void Chain_MultiStep_ExecutesInOrder()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 7, Title = "mage" }, 1);
            SaveMigrationManager.Register(new RetypeCoinsMigrator()); // 故意先注册 2→3
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.CurrentVersion = 3;

            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(1, RenameGoldMigrator.RunCount);
            Assert.AreEqual(1, RetypeCoinsMigrator.RunCount, "链应按序执行 1→2→3（改名先于改型，否则改型找不到键）");

            SaveMetadata metadata = ReadMeta("slot");
            Assert.AreEqual(3, metadata.SaveVersion);
            Assert.AreEqual(2, metadata.MigrationHistory.Count, "两步迁移各记一条审计");
            Assert.IsTrue(metadata.MigrationHistory[0].StartsWith("1->2|", StringComparison.Ordinal));
            Assert.IsTrue(metadata.MigrationHistory[1].StartsWith("2->3|", StringComparison.Ordinal));
        }

        [Test]
        public void Chain_MissingLink_ReturnsMigrationFailed()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 1 }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator()); // 仅 1→2，缺 2→3
            SaveMigrationManager.CurrentVersion = 3;

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _);

            Assert.AreEqual(SaveError.MigrationFailed, error, "缺链应 fail-fast");
            AssertErrorLogged("migration chain broken");
            Assert.IsTrue(_loadFailedEvents.Exists(e => e.Stage == ESaveFailureStage.Migrate && e.Error == SaveError.MigrationFailed), "应触发 Migrate 阶段失败事件");
        }

        [Test]
        public void Chain_Downgrade_ReturnsUnsupportedVersion()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 1 }, 3);
            SaveMigrationManager.CurrentVersion = 2;

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _);

            Assert.AreEqual(SaveError.UnsupportedVersion, error, "存档版本高于当前版本应拒绝降级");
            AssertErrorLogged("downgrade rejected");
        }

        [Test]
        public void Chain_AmbiguousEdge_ReturnsMigrationFailed()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 1 }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator());   // 1→2
            SaveMigrationManager.Register(new AmbiguousForkMigrator()); // 1→9，与 1→2 歧义
            SaveMigrationManager.CurrentVersion = 9;

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _);

            Assert.AreEqual(SaveError.MigrationFailed, error, "同起始版本多条不同目标边应 fail-fast");
        }

        /// <summary>歧义边探针（1→9）。</summary>
        internal sealed class AmbiguousForkMigrator : ISaveMigrator
        {
            public int FromVersion => 1;
            public int ToVersion => 9;
            public int Priority => 0;
            public UniTask Migrate(SaveMigrationContext context) => UniTask.CompletedTask;
        }

        [Test]
        public void Register_InvalidEdge_Throws()
        {
            Assert.Throws<ArgumentException>(() => SaveMigrationManager.Register(new InvalidEdgeMigrator()), "ToVersion <= FromVersion 应注册期 fail-fast");
            Assert.Throws<ArgumentNullException>(() => SaveMigrationManager.Register(null));
        }

        [Test]
        public void Chain_SameEdge_PriorityAscendingOrder()
        {
            // 边 5→6 上两个迁移器：Priority 1 应先于 Priority 10（注册顺序故意颠倒）
            WriteSlotAtVersion("slot", new ProfileData { Gold = 1 }, 5);
            SaveMigrationManager.Register(new PriorityLateMigrator());
            SaveMigrationManager.Register(new PriorityEarlyMigrator());
            SaveMigrationManager.CurrentVersion = 6;

            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(new[] { "early", "late" }, PriorityLateMigrator.ExecutionOrder.ToArray(), "同边迁移器应按 Priority 升序执行");
        }

        [Test]
        public void Chain_MigratorThrows_ReturnsMigrationFailed()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 1 }, 1);
            SaveMigrationManager.Register(new ThrowingMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _);

            Assert.AreEqual(SaveError.MigrationFailed, error);
            AssertErrorLogged("Save migrator");
        }

        [Test]
        public void Chain_AsyncViolatingMigrator_ReturnsMigrationFailed()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 1 }, 1);
            SaveMigrationManager.Register(new AsyncViolatingMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _);

            Assert.AreEqual(SaveError.MigrationFailed, error, "返回未完成任务的迁移器应 fail-fast（迁移须同步完成）");
        }

        #endregion

        #region 上下文操作 [CONTEXT OPS]

        [Test]
        public void Context_JsonRenameAndRetype_Applied()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 100, Title = "old" }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.Register(new RetypeCoinsMigrator());
            SaveMigrationManager.CurrentVersion = 3;

            SaveError error = _handler.TryLoadBlockCore<ProfileDataV3>(Paths("slot"), "profile", out ProfileDataV3 loaded);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual("coins-100", loaded.Coins, "改名+改型后应以新形态读出");
            Assert.AreEqual("old", loaded.Title, "未涉及字段不受影响");
        }

        [Test]
        public void Context_TransformBlock_AppliesWholeObjectTransform()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 5, Title = "x10" }, 1);
            SaveMigrationManager.Register(new TransformBlockMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out ProfileData loaded);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(50, loaded.Gold, "整块变换应生效");
            Assert.AreEqual("x10", loaded.Title);
        }

        [Test]
        public void Context_KvRenameAndRetype_Applied()
        {
            // KVT 载荷直测（组件块格式）：字段 hp（Int32）→ Hp 改名，mana（Int32）→ string 改型
            var writer = new SaveKeyValueWriter(64);
            writer.WriteInt32("hp", 66);
            writer.WriteInt32("mana", 7);
            byte[] source = writer.ToArray();

            bool renamed = SaveKvTransformer.RenameField(source, "hp", "Hp", out byte[] renamedBytes);
            Assert.IsTrue(renamed);

            bool retyped = SaveKvTransformer.RetypeField(renamedBytes, "mana", old => "mana-" + old, ESaveKvType.String, out byte[] finalBytes);
            Assert.IsTrue(retyped);

            var reader = new SaveKeyValueReader(finalBytes);
            Assert.IsTrue(reader.ReadRecord(out ReadOnlySpan<byte> key1, out ESaveKvType type1));
            Assert.AreEqual("Hp", System.Text.Encoding.UTF8.GetString(key1));
            Assert.AreEqual(ESaveKvType.Int32, type1);
            Assert.AreEqual(66, reader.ReadInt32());
            Assert.IsTrue(reader.ReadRecord(out ReadOnlySpan<byte> key2, out ESaveKvType type2));
            Assert.AreEqual("mana", System.Text.Encoding.UTF8.GetString(key2));
            Assert.AreEqual(ESaveKvType.String, type2);
            Assert.AreEqual("mana-7", reader.ReadString());
        }

        [Test]
        public void Context_KvRename_RecursesNestedScopes()
        {
            // 嵌套对象作用域内的字段键同样改名（组件绑定作用域结构模拟）
            var writer = new SaveKeyValueWriter(64);
            writer.BeginNestedObject("Component.Type.Name", 2);
            writer.WriteInt32("hp", 12);
            writer.WriteSingle("speed", 3f);
            writer.EndNested();

            bool hit = SaveKvTransformer.RenameField(writer.ToArray(), "hp", "Hp", out byte[] result);
            Assert.IsTrue(hit);

            var reader = new SaveKeyValueReader(result);
            Assert.IsTrue(reader.ReadRecord(out _, out ESaveKvType scopeType));
            Assert.AreEqual(ESaveKvType.Object, scopeType);
            Assert.AreEqual(2, reader.ReadChildCount());
            Assert.IsTrue(reader.ReadRecord(out ReadOnlySpan<byte> key1, out _));
            Assert.AreEqual("Hp", System.Text.Encoding.UTF8.GetString(key1), "嵌套作用域内键应被改名");
            Assert.AreEqual(12, reader.ReadInt32());
            Assert.IsTrue(reader.ReadRecord(out ReadOnlySpan<byte> key2, out _));
            Assert.AreEqual("speed", System.Text.Encoding.UTF8.GetString(key2), "未命中键不受影响");
        }

        [Test]
        public void Context_FieldOps_OnMissingTarget_AreNoOps()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 1 }, 1);
            SaveMigrationManager.Register(new NoOpAbsentMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out ProfileData loaded);

            Assert.AreEqual(SaveError.None, error, "目标块/字段不存在的操作应为无操作（兼容从未写过该块的旧档）");
            Assert.AreEqual(1, loaded.Gold);
        }

        /// <summary>无操作迁移器（操作全部指向不存在的块/字段）。</summary>
        internal sealed class NoOpAbsentMigrator : ISaveMigrator
        {
            public int FromVersion => 1;
            public int ToVersion => 2;
            public int Priority => 0;

            public UniTask Migrate(SaveMigrationContext context)
            {
                Assert.IsFalse(context.RenameBlock("missing-block", "x"));
                Assert.IsFalse(context.DeleteBlock("missing-block"));
                Assert.IsFalse(context.RenameField("missing-block", "a", "b"));
                Assert.IsFalse(context.RenameField("profile", "MissingField", "x"));
                Assert.IsFalse(context.TransformBlock<ProfileData>("missing-block", d => d));
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void Context_RenameBlock_Works()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 3 }, 1);
            SaveMigrationManager.Register(new RenameBlockMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            Assert.AreEqual(SaveError.FileNotFound, _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile", out _), "旧键应已不存在");
            SaveError error = _handler.TryLoadBlockCore<ProfileData>(Paths("slot"), "profile_v2", out ProfileData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(3, loaded.Gold);
        }

        /// <summary>块改名迁移器。</summary>
        internal sealed class RenameBlockMigrator : ISaveMigrator
        {
            public int FromVersion => 1;
            public int ToVersion => 2;
            public int Priority => 0;

            public UniTask Migrate(SaveMigrationContext context)
            {
                Assert.IsTrue(context.RenameBlock("profile", "profile_v2"));
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void Context_RenameFieldByType_ResolvesDeclaredKey()
        {
            SaveMigrationManager.CurrentVersion = 1;
            _handler.SaveBlockCore(Paths("slot"), "hero", new HeroData { Level = 9 }, ESaveBackend.Json, 1, CancellationToken.None);
            SaveMigrationManager.Register(new RenameByTypeMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            SaveError error = _handler.TryLoadBlockCore<HeroDataV2>(Paths("slot"), "hero", out HeroDataV2 loaded);

            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(9, loaded.level, "经类型声明键解析的改名应生效");
        }

        /// <summary>RenameField&lt;TData&gt; 键解析迁移器。</summary>
        internal sealed class RenameByTypeMigrator : ISaveMigrator
        {
            public int FromVersion => 1;
            public int ToVersion => 2;
            public int Priority => 0;

            public UniTask Migrate(SaveMigrationContext context)
            {
                Assert.IsTrue(context.RenameField<HeroData>("Level", "level"));
                return UniTask.CompletedTask;
            }
        }

        #endregion

        #region 回写与盖章 [WRITE-BACK / STAMP]

        [Test]
        public void WriteBack_Disabled_KeepsFileVersionButMigratesInMemory()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 8 }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.CurrentVersion = 2;
            _handler.MigrationWriteBack = false;

            SaveError error = _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out ProfileDataV2 loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(8, loaded.Coins, "内存数据应已迁移");

            // 会话缓存：同文件同会话不重复迁移（元数据直读被短路——读到的是盘上旧版本）
            SaveMetadata metadata = ReadMeta("slot");
            Assert.AreEqual(1, metadata.SaveVersion, "回写关闭时盘上版本保持旧值");

            // 新会话（缓存清空）再次加载 → 迁移链重跑
            SaveMigrationManager.ResetForTests();
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.CurrentVersion = 2;
            SaveError secondError = _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out _);
            Assert.AreEqual(SaveError.None, secondError);
            Assert.AreEqual(2, RenameGoldMigrator.RunCount, "回写关闭时跨会话重跑迁移链（预期行为）");
        }

        [Test]
        public void Stamp_FreshSave_WritesCurrentVersionIntoMeta()
        {
            SaveMigrationManager.CurrentVersion = 4;
            _handler.SaveBlockCore(Paths("slot"), "profile", new ProfileData { Gold = 1 }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveMetadata metadata = ReadMeta("slot");
            Assert.AreEqual(4, metadata.SaveVersion, "版本化激活时写入应自动盖章当前版本");
            Assert.IsNull(metadata.MigrationHistory, "未迁移的新档无迁移历史");
        }

        [Test]
        public void WritePath_OldFile_HealsBeforeMerge()
        {
            // 旧档（版本 1）直接写入新块（未经加载）→ 写入自愈：文件先迁移再合并，落盘即当前版本
            WriteSlotAtVersion("slot", new ProfileData { Gold = 2 }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.CurrentVersion = 2;

            _handler.SaveBlockCore(Paths("slot"), "other", new HeroData { Level = 5 }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveMetadata metadata = ReadMeta("slot");
            Assert.AreEqual(2, metadata.SaveVersion, "写入自愈后落盘版本应为当前版本");
            Assert.AreEqual(1, RenameGoldMigrator.RunCount, "写入路径应执行一次迁移");

            // 旧块已按新形态落盘（Coins 键存在）
            SaveError error = _handler.TryLoadBlockCore<ProfileDataV2>(Paths("slot"), "profile", out ProfileDataV2 loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(2, loaded.Coins, "写入自愈后旧块已按新形态落盘");
        }

        [Test]
        public void MigrateSave_Explicit_HealsWithoutLoad()
        {
            WriteSlotAtVersion("slot", new ProfileData { Gold = 4 }, 1);
            SaveMigrationManager.Register(new RenameGoldMigrator());
            SaveMigrationManager.CurrentVersion = 2;
            _handler.MigrationWriteBack = false; // 显式调用强制回写，不受配置约束

            SaveError error = _handler.MigrateSave("slot", TestFolder);

            Assert.AreEqual(SaveError.None, error);
            SaveMetadata metadata = ReadMeta("slot");
            Assert.AreEqual(2, metadata.SaveVersion, "显式迁移应强制回写（即使回写设置关闭）");
        }

        [Test]
        public void MigrateSave_MissingFile_ReturnsFileNotFound()
        {
            SaveMigrationManager.CurrentVersion = 2;
            SaveError error = _handler.MigrateSave("no-such-slot", TestFolder);
            Assert.AreEqual(SaveError.FileNotFound, error);
        }

        [Test]
        public void MigrateSave_InactiveBus_IsNoOp()
        {
            // 迁移总线未激活（CurrentVersion = 0）：显式调用无操作且不创建元数据块
            _handler.SaveBlockCore(Paths("slot"), "profile", new ProfileData { Gold = 1 }, ESaveBackend.Json, 1, CancellationToken.None);
            SaveError error = _handler.MigrateSave("slot", TestFolder);
            Assert.AreEqual(SaveError.None, error);
            SaveError metaError = _handler.TryLoadBlockCore<SaveMetadata>(Paths("slot"), SaveServiceHandler.META_BLOCK_KEY, out _);
            Assert.AreEqual(SaveError.FileNotFound, metaError, "未激活时不应创建元数据块");
        }

        [Test]
        public void Facade_MigrateSave_HandlerNotReady()
        {
            SetFacadeHandler(null);
            try
            {
                Assert.AreEqual(SaveError.HandlerNotReady, SaveService.MigrateSave("slot", TestFolder), "处理器未就绪应降级 HandlerNotReady");
            }
            finally
            {
                SetFacadeHandler(null);
            }
        }

        /// <summary>
        /// 经生成的 <c>Internal_UseHandler</c> 直设外观处理器（Handler 属性 setter 拒绝 null）。
        /// </summary>
        private static void SetFacadeHandler(SaveServiceHandler value) => SaveService.Internal_UseHandler(value);

        #endregion

        #region 组件模式版本 [COMPONENT SCHEMA]

        [Test]
        public void Component_SchemaMismatch_RoutesToMigratorHook()
        {
            var gameObject = new GameObject("schema-test");
            try
            {
                var probe = gameObject.AddComponent<SchemaProbeComponent>();
                var saveComponent = gameObject.AddComponent<SaveComponent>();
                saveComponent.BlockKey = "scene:probe";
                saveComponent.Targets.Add(new SaveTargetBinding { Target = probe, EnabledFields = new List<string> { "Value" } });
                if (!SaveCapturerRegistry.TryGet(typeof(SchemaProbeComponent), out _))
                {
                    SaveCapturerRegistry.Register(typeof(SchemaProbeComponent), new SchemaProbeFallbackCapturer());
                }

                // 构造 v1 旧档 KVT：$schemas 记录版本 1 + 组件作用域内旧键 legacy_value（字符串）
                var writer = new SaveKeyValueWriter(128);
                writer.BeginNestedObject(SaveComponent.SchemaScopeKey, 1);
                writer.WriteInt32(typeof(SchemaProbeComponent).FullName, 1);
                writer.EndNested();
                writer.BeginNestedObject(typeof(SchemaProbeComponent).FullName, 1);
                writer.WriteString("legacy_value", "carried-over");
                writer.EndNested();
                byte[] kvt = writer.ToArray();

                var reader = new SaveKeyValueReader(kvt);
                saveComponent.Restore(ref reader);

                Assert.AreEqual(1, probe.MigratedFrom, "版本不符应路由到迁移钩子并携带旧版本号");
                Assert.AreEqual("carried-over", probe.MigratedText, "迁移钩子应以旧键读出数据");
                Assert.AreEqual(0, probe.Value, "钩子路径不经标准恢复");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Component_SchemaMatch_StandardRestore()
        {
            var gameObject = new GameObject("schema-match-test");
            try
            {
                var probe = gameObject.AddComponent<SchemaProbeComponent>();
                var saveComponent = gameObject.AddComponent<SaveComponent>();
                saveComponent.Targets.Add(new SaveTargetBinding { Target = probe, EnabledFields = new List<string> { "Value" } });
                if (!SaveCapturerRegistry.TryGet(typeof(SchemaProbeComponent), out _))
                {
                    SaveCapturerRegistry.Register(typeof(SchemaProbeComponent), new SchemaProbeFallbackCapturer());
                }

                // 当前版本（2）捕获 → 恢复往返
                var writer = new SaveKeyValueWriter(128);
                saveComponent.Targets[0] = new SaveTargetBinding { Target = probe, EnabledFields = new List<string> { "Value" } };
                probe.Value = 55;
                saveComponent.Capture(ref writer);
                probe.Value = 0;

                var reader = new SaveKeyValueReader(writer.ToArray());
                saveComponent.Restore(ref reader);

                Assert.AreEqual(55, probe.Value, "版本一致应走标准恢复");
                Assert.AreEqual(-1, probe.MigratedFrom, "版本一致不应触发迁移钩子");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Component_MissingSchemaRecord_TreatedAsCurrent()
        {
            var gameObject = new GameObject("schema-legacy-test");
            try
            {
                var probe = gameObject.AddComponent<SchemaProbeComponent>();
                var saveComponent = gameObject.AddComponent<SaveComponent>();
                saveComponent.Targets.Add(new SaveTargetBinding { Target = probe, EnabledFields = new List<string> { "Value" } });
                if (!SaveCapturerRegistry.TryGet(typeof(SchemaProbeComponent), out _))
                {
                    SaveCapturerRegistry.Register(typeof(SchemaProbeComponent), new SchemaProbeFallbackCapturer());
                }

                // 旧格式（无 $schemas 作用域）→ 按当前版本标准恢复
                var writer = new SaveKeyValueWriter(64);
                writer.BeginNestedObject(typeof(SchemaProbeComponent).FullName, 1);
                writer.WriteInt32("Value", 33);
                writer.EndNested();

                var reader = new SaveKeyValueReader(writer.ToArray());
                saveComponent.Restore(ref reader);

                Assert.AreEqual(33, probe.Value);
                Assert.AreEqual(-1, probe.MigratedFrom);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        #endregion
    }
}
