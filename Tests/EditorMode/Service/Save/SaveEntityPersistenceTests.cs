using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Service.Save
{
    /// <summary>
    /// 动态实体持久化闭环测试：生成（注入 ID/块键/命名规整）→ 差分捕获（体积与内容）→ 销毁标记 →
    /// 恢复（原 ID/字段值/模板默认/父子接线/EntityRestored 事件）→ 陈旧块清理 → 加载失败降级。
    /// <para>模板加载器注入假实现（绕开 ResourceService/EditMode 限制）；模板源保持未激活避免 Awake 注册污染。
    /// 告警/错误断言经 <see cref="LogUtility.OnMessageLogged"/> 事件捕获（Handler 无关）；UTF 可见链路另补
    /// <c>LogAssert.Expect</c>（黑名单：is not UnityLoggingHandler）。</para>
    /// </summary>
    public partial class SaveEntityPersistenceTests
    {
        /// <summary>
        /// 实体测试组件（public 字段 = Unity 可序列化——Instantiate 克隆携带模板值；SG 生成捕获器经模块初始化器注册；嵌套链全 partial 为生成器约束）。
        /// </summary>
        public partial class EntityTestComponent : MonoBehaviour
        {
            [SaveField]
            public int Hp;

            [SaveField("player_name")]
            public string PlayerName;

            [SaveField]
            public float Speed;
        }

        /// <summary>注册键：测试模板。</summary>
        private const string ENEMY_KEY = "enemy";

        private readonly List<GameObject> _objects = new List<GameObject>();
        private GameObject _prefabSource;
        private List<(ELogLevel Level, string Message)> _capturedLogs;
        private List<SaveEntityRestoredArgs> _restoredEvents;

        [SetUp]
        public void SetUp()
        {
            _capturedLogs = new List<(ELogLevel, string)>();
            _restoredEvents = new List<SaveEntityRestoredArgs>();
            LogUtility.OnMessageLogged += CaptureLog;
            SaveService.EntityRestored += OnEntityRestored;
            SaveEntityRegistry.ResetTables();
            SaveEntityPersistence.ResetForTests();
            SaveBuiltInCapturers.RegisterBuiltIns();

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(SaveEntityPersistenceTests.EntityTestComponent), out _),
                "测试组件捕获器应已由 SaveHost SG 生成并注册");

            // 模板源：未激活（Awake 不跑——克隆捕获与生成均不染指注册表）
            _prefabSource = new GameObject("entity-src-enemy");
            _prefabSource.SetActive(false);
            var kv = _prefabSource.AddComponent<SaveEntityPersistenceTests.EntityTestComponent>();
            kv.Hp = 100;
            kv.PlayerName = "goblin";
            _prefabSource.AddComponent<SaveObjectIdentity>();
            var saveComponent = _prefabSource.AddComponent<SaveComponent>();
            saveComponent.Targets.Add(new SaveTargetBinding
            {
                Target = kv,
                EnabledFields = new List<string> { "Hp", "player_name" },
            });
            saveComponent.Targets.Add(new SaveTargetBinding
            {
                Target = _prefabSource.transform,
                EnabledFields = new List<string> { "localPosition", "localRotation", "localScale" },
            });

            SaveEntityPersistence.s_TemplateLoaderSync = FakeLoadTemplate;
            SaveEntityPersistence.s_TemplateLoaderAsync = (key, _) => UniTask.FromResult(FakeLoadTemplate(key));
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            SaveService.EntityRestored -= OnEntityRestored;
            foreach (GameObject gameObject in _objects)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            _objects.Clear();
            if (_prefabSource != null)
            {
                Object.DestroyImmediate(_prefabSource);
                _prefabSource = null;
            }

            SaveEntityPersistence.ResetForTests();
            SaveEntityRegistry.ResetTables();
        }

        private void CaptureLog(ELogLevel level, string message, Exception exception)
        {
            _capturedLogs.Add((level, message));
        }

        private void OnEntityRestored(SaveEntityRestoredArgs args)
        {
            _restoredEvents.Add(args);
        }

        /// <summary>
        /// 假模板加载：仅识别 <see cref="ENEMY_KEY"/>，克隆模板源到 staging 下（未激活）；未登记键仿默认加载器记错误日志。
        /// </summary>
        private GameObject FakeLoadTemplate(string prefabKey)
        {
            if (prefabKey != ENEMY_KEY)
            {
                LogUtility.Error("[SaveService] Prefab key '{0}' is not registered (fake loader).", prefabKey);
                return null;
            }

            return Object.Instantiate(_prefabSource, SaveEntityPersistence.EnsureStaging().transform);
        }

        /// <summary>
        /// 为随后一条 Error 日志声明 UTF 预期。
        /// </summary>
        private static void ExpectErrorLogForUtf()
        {
            if (LogUtility.Handler is not UnityLoggingHandler)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
            }
        }

        /// <summary>
        /// 断言已记录包含指定片段的日志。
        /// </summary>
        private void AssertLogged(ELogLevel level, string fragment)
        {
            Assert.IsTrue(_capturedLogs.Exists(entry => entry.Level == level && entry.Message != null && entry.Message.Contains(fragment)),
                $"应记录含 '{fragment}' 的 {level} 日志，实际捕获 {_capturedLogs.Count} 条");
        }

        /// <summary>
        /// 条目列表转块字典（模拟档内读取结果）。
        /// </summary>
        private static Dictionary<string, byte[]> ToBlockDict(List<SaveBlockEntry> entries)
        {
            var blocks = new Dictionary<string, byte[]>(entries.Count);
            foreach (SaveBlockEntry entry in entries)
            {
                blocks[entry.Key] = entry.Bytes;
            }

            return blocks;
        }

        /// <summary>
        /// 收集 KVT 块指定作用域的记录键（按子项数精确消费）。
        /// </summary>
        private static List<string> CollectScopeKeys(byte[] bytes, string scopeKey)
        {
            var keys = new List<string>();
            var encoding = new UTF8Encoding(false);
            var reader = new SaveKeyValueReader(bytes);
            while (reader.ReadRecord(out ReadOnlySpan<byte> key, out ESaveKvType type))
            {
                if (type == ESaveKvType.Object && encoding.GetString(key) == scopeKey)
                {
                    int childCount = reader.ReadChildCount();
                    for (int i = 0; i < childCount && reader.ReadRecord(out ReadOnlySpan<byte> childKey, out ESaveKvType _); i++)
                    {
                        keys.Add(encoding.GetString(childKey));
                        reader.SkipRecordPayload();
                    }

                    return keys;
                }

                reader.SkipRecordPayload();
            }

            return keys;
        }

        [Test]
        public void InstantiatePersistent_InjectsIdentityAndRegistersSpawn()
        {
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, new Vector3(5f, 1f, 0f), Quaternion.Euler(0f, 90f, 0f));
            _objects.Add(entity);

            Assert.NotNull(entity);
            Assert.IsTrue(entity.activeSelf, "生成即激活");
            Assert.AreEqual("entity-src-enemy", entity.name, "克隆名应去 (Clone) 后缀");
            Assert.AreEqual(new Vector3(5f, 1f, 0f), entity.transform.position);

            SaveObjectIdentity identity = SaveObjectIdentity.Resolve(entity);
            Assert.NotNull(identity);
            Assert.IsFalse(string.IsNullOrEmpty(identity.Id), "生成注入稳定 ID");
            Assert.IsTrue(SaveEntityRegistry.TryFind(identity.Id, out SaveObjectIdentity found));
            Assert.AreSame(identity, found);

            SaveComponent saveComponent = entity.GetComponent<SaveComponent>();
            Assert.AreEqual(SaveEntityPersistence.BuildEntityBlockKey(identity.Id), saveComponent.ResolvedBlockKey, "块键应派生为 entity:{id}");
        }

        [Test]
        public void InstantiatePersistent_UnknownKey_ReturnsNullWithError()
        {
            ExpectErrorLogForUtf();
            GameObject entity = SaveService.InstantiatePersistent("missing", Vector3.zero, Quaternion.identity);
            Assert.IsNull(entity);
        }

        [Test]
        public void CaptureEntityEntries_WritesSparseDiffAndTable()
        {
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, new Vector3(5f, 0f, 0f), Quaternion.identity);
            _objects.Add(entity);
            string entityId = SaveObjectIdentity.Resolve(entity).Id;
            entity.GetComponent<SaveEntityPersistenceTests.EntityTestComponent>().Hp = 42;

            List<SaveBlockEntry> entries = SaveEntityPersistence.CaptureEntityEntries("slot1", "Save");
            Assert.AreEqual(2, entries.Count, "实体块 + 实体表块");

            // —— 差分内容：KvTestComponent 作用域只含 Hp；Transform 作用域只含 localPosition
            SaveBlockEntry entityEntry = entries[0];
            Assert.AreEqual(SaveEntityPersistence.BuildEntityBlockKey(entityId), entityEntry.Key);
            List<string> kvKeys = CollectScopeKeys(entityEntry.Bytes, typeof(SaveEntityPersistenceTests.EntityTestComponent).FullName);
            CollectionAssert.AreEquivalent(new[] { "Hp" }, kvKeys, "未变动字段不应进入差分块");
            List<string> transformKeys = CollectScopeKeys(entityEntry.Bytes, typeof(Transform).FullName);
            CollectionAssert.AreEquivalent(new[] { "localPosition" }, transformKeys);

            // —— 差分体积：单字段变动显著小于全量捕获
            var fullWriter = new SaveKeyValueWriter(256);
            entity.GetComponent<SaveComponent>().Capture(ref fullWriter);
            Assert.Less(entityEntry.Bytes.Length, fullWriter.ToArray().Length, "差分块必须小于全量捕获");

            // —— 实体表：一条生成记录，字段齐全
            SaveBlockEntry tableEntry = entries[1];
            Assert.AreEqual(SaveEntityPersistence.EntityTableBlockKey, tableEntry.Key);
            SaveEntityTable.Read(tableEntry.Bytes, out List<SaveSpawnRecord> spawns, out List<string> destroyed);
            Assert.AreEqual(1, spawns.Count);
            Assert.AreEqual(entityId, spawns[0].EntityId);
            Assert.AreEqual(ENEMY_KEY, spawns[0].PrefabKey);
            Assert.IsEmpty(destroyed);
        }

        [Test]
        public async Task SpawnCaptureDestroyRestore_RoundTripsEntityState()
        {
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, new Vector3(5f, 1f, 0f), Quaternion.Euler(0f, 90f, 0f));
            _objects.Add(entity);
            string entityId = SaveObjectIdentity.Resolve(entity).Id;
            entity.GetComponent<SaveEntityPersistenceTests.EntityTestComponent>().Hp = 42;

            List<SaveBlockEntry> entries = SaveEntityPersistence.CaptureEntityEntries("slot1", "Save");
            Dictionary<string, byte[]> blocks = ToBlockDict(entries);

            SaveService.DestroyPersistent(entity);
            Assert.IsTrue(entity == null, "实体已销毁");

            await SaveEntityPersistence.RestoreFromBlocksAsync(blocks, "slot1", "Save", default).AsTask();

            Assert.IsTrue(SaveEntityRegistry.TryFind(entityId, out SaveObjectIdentity restored), "原 ID 恢复");
            _objects.Add(restored.gameObject);
            var kv = restored.GetComponent<SaveEntityPersistenceTests.EntityTestComponent>();
            Assert.AreEqual(42, kv.Hp, "差分字段恢复");
            Assert.AreEqual("goblin", kv.PlayerName, "未变动字段保持模板默认");
            Assert.AreEqual(new Vector3(5f, 1f, 0f), restored.transform.localPosition, "变换差分恢复");
            Assert.AreEqual(90f, restored.transform.localEulerAngles.y, 0.01f);

            Assert.AreEqual(1, _restoredEvents.Count, "EntityRestored 事件应逐实体触发");
            Assert.AreEqual(entityId, _restoredEvents[0].EntityId);
            Assert.AreEqual(ENEMY_KEY, _restoredEvents[0].PrefabKey);
            Assert.AreSame(restored.gameObject, _restoredEvents[0].Instance);
        }

        [Test]
        public async Task RestoreEntities_WiresParentChildInSecondPass()
        {
            GameObject parent = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(parent);
            string parentId = SaveObjectIdentity.Resolve(parent).Id;
            GameObject child = SaveService.InstantiatePersistent(ENEMY_KEY, new Vector3(1f, 0f, 0f), Quaternion.identity, parent.transform);
            _objects.Add(child);
            string childId = SaveObjectIdentity.Resolve(child).Id;
            child.GetComponent<SaveEntityPersistenceTests.EntityTestComponent>().Hp = 7;

            List<SaveBlockEntry> entries = SaveEntityPersistence.CaptureEntityEntries("slot1", "Save");
            SaveEntityTable.Read(entries[entries.Count - 1].Bytes, out List<SaveSpawnRecord> spawns, out _);
            Assert.AreEqual(2, spawns.Count);
            Assert.AreEqual(parentId, spawns[1].ParentId, "子实体记录父级 ID");

            Dictionary<string, byte[]> blocks = ToBlockDict(entries);
            SaveService.DestroyPersistent(child);
            SaveService.DestroyPersistent(parent);

            await SaveEntityPersistence.RestoreFromBlocksAsync(blocks, "slot1", "Save", default).AsTask();

            Assert.IsTrue(SaveEntityRegistry.TryFind(parentId, out SaveObjectIdentity restoredParent));
            Assert.IsTrue(SaveEntityRegistry.TryFind(childId, out SaveObjectIdentity restoredChild));
            _objects.Add(restoredParent.gameObject);
            _objects.Add(restoredChild.gameObject);
            Assert.AreSame(restoredParent.transform, restoredChild.transform.parent, "父子关系第二轮接线");
            Assert.AreEqual(7, restoredChild.GetComponent<SaveEntityPersistenceTests.EntityTestComponent>().Hp);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), restoredChild.transform.localPosition, "父级下的局部坐标恢复");
        }

        [Test]
        public async Task DestroyPersistent_PresetObject_RecordedAndDestroyedOnRestore()
        {
            // 预置对象（编辑模式 Awake 不执行——手动注册模拟播放态）
            var preset = new GameObject("entity-preset");
            _objects.Add(preset);
            var identity = preset.AddComponent<SaveObjectIdentity>();
            identity.m_Id = "preset-1";
            SaveEntityRegistry.Register(identity);

            SaveService.DestroyPersistent(preset);
            Assert.IsTrue(preset == null);

            List<SaveBlockEntry> entries = SaveEntityPersistence.CaptureEntityEntries("slot1", "Save");
            SaveEntityTable.Read(entries[entries.Count - 1].Bytes, out _, out List<string> destroyed);
            CollectionAssert.Contains(destroyed, "preset-1", "预置对象销毁应记入销毁表");

            // 模拟场景重载后同名预置对象回归——恢复时按销毁表销毁
            var presetReborn = new GameObject("entity-preset-reborn");
            _objects.Add(presetReborn);
            var rebornIdentity = presetReborn.AddComponent<SaveObjectIdentity>();
            rebornIdentity.m_Id = "preset-1";
            SaveEntityRegistry.Register(rebornIdentity);

            await SaveEntityPersistence.RestoreFromBlocksAsync(ToBlockDict(entries), "slot1", "Save", default).AsTask();
            Assert.IsTrue(presetReborn == null, "恢复管线应销毁销毁表内的预置对象");
        }

        [Test]
        public void ComputeStaleEntityKeys_RemovedSpawnAndOrphan_Returned()
        {
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(entity);
            string entityId = SaveObjectIdentity.Resolve(entity).Id;
            List<SaveBlockEntry> entries = SaveEntityPersistence.CaptureEntityEntries("slot1", "Save");
            Dictionary<string, byte[]> blocks = ToBlockDict(entries);
            blocks["entity:orphan-1"] = new byte[] { 0 }; // 两表均无登记的孤儿块

            SaveService.DestroyPersistent(entity);

            List<string> staleKeys = SaveEntityPersistence.ComputeStaleEntityKeys(blocks);
            CollectionAssert.Contains(staleKeys, SaveEntityPersistence.BuildEntityBlockKey(entityId), "档有会话无 = 陈旧");
            CollectionAssert.Contains(staleKeys, "entity:orphan-1", "孤儿块应清理");
        }

        [Test]
        public void SpawnTwoSamePrefab_DistinctBlockKeys()
        {
            GameObject first = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(first);
            GameObject second = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.one, Quaternion.identity);
            _objects.Add(second);

            string firstKey = first.GetComponent<SaveComponent>().ResolvedBlockKey;
            string secondKey = second.GetComponent<SaveComponent>().ResolvedBlockKey;
            Assert.AreNotEqual(firstKey, secondKey, "同预制体同名的两个实体块键必须不同（GUID 天然防撞键）");
        }

        [Test]
        public async Task RestoreEntities_LoadFailure_PreservesRecordAndLogsError()
        {
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(entity);
            string entityId = SaveObjectIdentity.Resolve(entity).Id;
            List<SaveBlockEntry> entries = SaveEntityPersistence.CaptureEntityEntries("slot1", "Save");
            Dictionary<string, byte[]> blocks = ToBlockDict(entries);
            SaveService.DestroyPersistent(entity);

            // 加载器改为全失败——生成跳过但记录保留（CarryForward）
            ExpectErrorLogForUtf();
            SaveEntityPersistence.s_TemplateLoaderAsync = (_, __) => UniTask.FromResult<GameObject>(null);
            await SaveEntityPersistence.RestoreFromBlocksAsync(blocks, "slot1", "Save", default).AsTask();

            Assert.IsFalse(SaveEntityRegistry.TryFind(entityId, out _), "生成失败无实例");
            AssertLogged(ELogLevel.Error, "Failed to load prefab");

            // 记录保留 → 再捕获实体表仍在册（原块不因后续保存丢失）
            List<SaveBlockEntry> recaptured = SaveEntityPersistence.CaptureEntityEntries("slot1", "Save");
            SaveEntityTable.Read(recaptured[recaptured.Count - 1].Bytes, out List<SaveSpawnRecord> spawns, out _);
            Assert.AreEqual(1, spawns.Count);
            Assert.AreEqual(entityId, spawns[0].EntityId, "生成失败的实体记录应保留");
        }
    }
}
