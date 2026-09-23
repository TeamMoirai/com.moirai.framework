using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UObject = UnityEngine.Object;

namespace Service.Save
{
    /// <summary>
    /// 实体增量保存闭环测试（独立夹具——执行模型统一为 [UnityTest] 协程，避免与 async Task 用例混跑的线程归属/事件派发串扰）：
    /// 零变化零 IO 跳过、字段变化仅写脏块、销毁清理陈旧块、外部改写守卫失效走全量、恢复后基准失效走全量。
    /// <para>模板加载器注入假实现（绕开 ResourceService/EditMode 限制）；全流程真实文件 IO（<c>s_OverrideBasePath</c> 指向临时目录）；
    /// 外观处理器经 <see cref="SetFacadeHandler"/> 反射注入（生成的 Handler 属性 setter 拒绝 null）。</para>
    /// </summary>
    public class SaveEntityIncrementalTests
    {
        /// <summary>注册键：测试模板。</summary>
        private const string ENEMY_KEY = "enemy";

        private readonly List<GameObject> _objects = new List<GameObject>();
        private GameObject _prefabSource;
        private List<(ELogLevel Level, string Message)> _capturedLogs;
        private List<SaveBlockChangedArgs> _blockSavedEvents;
        private List<SaveSlotChangedArgs> _slotChangedEvents;
        private PlainSaveHandler _handler;
        private string _rootPath;

        [SetUp]
        public void SetUp()
        {
            _capturedLogs = new List<(ELogLevel, string)>();
            _blockSavedEvents = new List<SaveBlockChangedArgs>();
            _slotChangedEvents = new List<SaveSlotChangedArgs>();
            LogUtility.OnMessageLogged += CaptureLog;
            SaveService.BlockSaved += OnBlockSaved;
            SaveService.SlotChanged += OnSlotChanged;
            SaveEntityRegistry.ResetTables();
            SaveEntityPersistence.ResetForTests();
            SaveBuiltInCapturers.RegisterBuiltIns();

            Assert.IsTrue(SaveCapturerRegistry.TryGet(typeof(SaveEntityPersistenceTests.EntityTestComponent), out _),
                "测试组件捕获器应已由 SaveHost SG 生成并注册");

            // 模板源：未激活（Awake 不跑——克隆捕获与生成均不染指注册表）
            _prefabSource = new GameObject("entity-incr-src-enemy");
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

            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-entity-incr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            SaveService.BlockSaved -= OnBlockSaved;
            SaveService.SlotChanged -= OnSlotChanged;
            foreach (GameObject gameObject in _objects)
            {
                if (gameObject != null)
                {
                    UObject.DestroyImmediate(gameObject);
                }
            }

            _objects.Clear();
            if (_prefabSource != null)
            {
                UObject.DestroyImmediate(_prefabSource);
                _prefabSource = null;
            }

            SetFacadeHandler(null);
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

            SaveEntityPersistence.ResetForTests();
            SaveEntityRegistry.ResetTables();
        }

        private void CaptureLog(ELogLevel level, string message, Exception exception)
        {
            _capturedLogs.Add((level, message));
        }

        private void OnBlockSaved(SaveBlockChangedArgs args)
        {
            _blockSavedEvents.Add(args);
        }

        private void OnSlotChanged(SaveSlotChangedArgs args)
        {
            _slotChangedEvents.Add(args);
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
        /// 假模板加载：仅识别 <see cref="ENEMY_KEY"/>，克隆模板源到 staging 下（未激活）。
        /// </summary>
        private GameObject FakeLoadTemplate(string prefabKey)
        {
            if (prefabKey != ENEMY_KEY)
            {
                LogUtility.Error("[SaveService] Prefab key '{0}' is not registered (fake loader).", prefabKey);
                return null;
            }

            GameObject clone = UObject.Instantiate(_prefabSource, SaveEntityPersistence.EnsureStaging().transform);
            return clone;
        }

        /// <summary>
        /// 直设外观处理器（生成的 Handler 属性 setter 拒绝 null，降级注入走 <c>Internal_UseHandler</c>）。
        /// </summary>
        private static void SetFacadeHandler(SaveServiceHandler handler) => SaveService.Internal_UseHandler(handler);

        /// <summary>
        /// 主线程等待异步任务完成（逐帧 yield 保持编辑器泵——工作线程入队的事件/日志在主线程派发窗口内到达；
        /// 协程用例的线程归属与派发时序确定性依赖本模式，禁止改用 async Task + AsTask await——EditMode 无 SyncContext，续体落线程池会触发 Unity API 主线程违例）。
        /// </summary>
        private static IEnumerator WaitForTask(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            // 再等一帧：merge/恢复的工作线程事件（BlockSaved/SlotChanged/Error 日志）入队后在主线程下一帧派发——确保落入本用例窗口
            yield return null;

            if (task.IsFaulted && task.Exception != null)
            {
                throw task.Exception.InnerException ?? task.Exception;
            }
        }

        [UnityTest]
        public IEnumerator SaveEntities_UnchangedSecondSave_SkipsWriteEntirely()
        {
            SetFacadeHandler(_handler);
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(entity);

            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());
            var paths = SaveServiceHandler.ResolveSavePaths("slot-incr", "Save");
            Assert.IsTrue(File.Exists(paths.SaveFilePath), "首次全量写回应落档");
            DateTime firstWriteTime = File.GetLastWriteTimeUtc(paths.SaveFilePath);
            _blockSavedEvents.Clear();
            _slotChangedEvents.Clear();

            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());

            Assert.AreEqual(firstWriteTime, File.GetLastWriteTimeUtc(paths.SaveFilePath), "零变化保存应零 IO 跳过（档写入时间不变）");
            Assert.AreEqual(0, _blockSavedEvents.Count, "跳过保存不触发 BlockSaved");
            Assert.AreEqual(0, _slotChangedEvents.Count, "跳过保存不触发 SlotChanged");
        }

        [UnityTest]
        public IEnumerator SaveEntities_FieldChanged_WritesOnlyDirtyBlock()
        {
            SetFacadeHandler(_handler);
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(entity);
            string entityId = SaveObjectIdentity.Resolve(entity).Id;

            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());
            _blockSavedEvents.Clear();

            entity.GetComponent<SaveEntityPersistenceTests.EntityTestComponent>().Hp = 42;
            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());

            Assert.AreEqual(1, _blockSavedEvents.Count, "仅变化块触发 BlockSaved（实体表未变）");
            Assert.AreEqual(SaveEntityPersistence.BuildEntityBlockKey(entityId), _blockSavedEvents[0].Key);

            // 恢复链路验证增量写回的内容正确（差分更新 Hp=42）
            SaveService.DestroyPersistent(entity);
            yield return WaitForTask(SaveService.RestoreEntitiesAsync("slot-incr", "Save").AsTask());
            Assert.IsTrue(SaveEntityRegistry.TryFind(entityId, out SaveObjectIdentity restored), "原 ID 恢复");
            _objects.Add(restored.gameObject);
            Assert.AreEqual(42, restored.GetComponent<SaveEntityPersistenceTests.EntityTestComponent>().Hp, "增量写回的差分应可恢复");
        }

        [UnityTest]
        public IEnumerator SaveEntities_AfterDestroy_RemovesStaleBlock()
        {
            SetFacadeHandler(_handler);
            GameObject entityA = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(entityA);
            GameObject entityB = SaveService.InstantiatePersistent(ENEMY_KEY, new Vector3(3f, 0f, 0f), Quaternion.identity);
            _objects.Add(entityB);
            string entityIdA = SaveObjectIdentity.Resolve(entityA).Id;
            string entityIdB = SaveObjectIdentity.Resolve(entityB).Id;

            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());

            SaveService.DestroyPersistent(entityA);
            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());

            var paths = SaveServiceHandler.ResolveSavePaths("slot-incr", "Save");
            Dictionary<string, byte[]> blocks = _handler.ReadRawBlocks(paths);
            Assert.IsFalse(blocks.ContainsKey(SaveEntityPersistence.BuildEntityBlockKey(entityIdA)), "已销毁实体块应被清理");
            Assert.IsTrue(blocks.ContainsKey(SaveEntityPersistence.BuildEntityBlockKey(entityIdB)), "存活实体块应保留");
        }

        [UnityTest]
        public IEnumerator SaveEntities_ExternallyOverwritten_FallsBackToFullMerge()
        {
            SetFacadeHandler(_handler);
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(entity);
            string entityId = SaveObjectIdentity.Resolve(entity).Id;

            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());
            var paths = SaveServiceHandler.ResolveSavePaths("slot-incr", "Save");

            // 外部改写档（写入时间失配）——守卫失效，本次保存须全量重写回正确内容
            File.WriteAllText(paths.SaveFilePath, "garbage-external-overwrite");
            ExpectErrorLogForUtf();
            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());

            Dictionary<string, byte[]> blocks = _handler.ReadRawBlocks(paths);
            Assert.IsTrue(blocks.ContainsKey(SaveEntityPersistence.ENTITY_TABLE_BLOCK_KEY), "全量重写后实体表块应在档");
            Assert.IsTrue(blocks.ContainsKey(SaveEntityPersistence.BuildEntityBlockKey(entityId)), "全量重写后实体块应在档");
        }

        [UnityTest]
        public IEnumerator SaveEntities_AfterRestore_FullMergeWritesAgain()
        {
            SetFacadeHandler(_handler);
            GameObject entity = SaveService.InstantiatePersistent(ENEMY_KEY, Vector3.zero, Quaternion.identity);
            _objects.Add(entity);

            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());
            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());
            Assert.AreEqual(1, _slotChangedEvents.Count, "二次无变化保存应跳过（仅首次 Saved 事件）");

            // 恢复替换会话状态——增量基准失效，再次保存（零字段变化）必须全量写回
            yield return WaitForTask(SaveService.RestoreEntitiesAsync("slot-incr", "Save").AsTask());
            _slotChangedEvents.Clear();

            yield return WaitForTask(SaveService.SaveEntitiesAsync("slot-incr", "Save").AsTask());

            Assert.AreEqual(1, _slotChangedEvents.Count, "恢复后保存应全量写回（基准失效）");
            Assert.AreEqual(ESaveSlotChangeKind.Saved, _slotChangedEvents[0].Kind);
        }
    }
}
