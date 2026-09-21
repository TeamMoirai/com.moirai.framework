using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EditorSceneManager = UnityEditor.SceneManagement.EditorSceneManager;
using NewSceneMode = UnityEditor.SceneManagement.NewSceneMode;
using NewSceneSetup = UnityEditor.SceneManagement.NewSceneSetup;
using OpenSceneMode = UnityEditor.SceneManagement.OpenSceneMode;

namespace Service.Save
{
    /// <summary>
    /// V3-P0 硬化回归测试：TransformPath 全路径撞键、文件头未知标志位拒载、并发门互斥与惰性回收。
    /// </summary>
    public class SaveV3HardeningTests
    {
        [Serializable]
        private sealed class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        private const string TestFolder = "Slots";

        private PlainSaveHandler _handler;
        private string _rootPath;
        private List<(ELogLevel Level, string Message)> _capturedLogs;
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _handler = new PlainSaveHandler();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-v3-hardening-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;

            _capturedLogs = new List<(ELogLevel, string)>();
            LogUtility.OnMessageLogged += CaptureLog;
        }

        [TearDown]
        public void TearDown()
        {
            LogUtility.OnMessageLogged -= CaptureLog;
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
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

        #region 层级路径 [TRANSFORM PATH]

        [Test]
        public void TransformPath_DeepNesting_ProducesFullPath()
        {
            GameObject a = Track(new GameObject("A"));
            GameObject b = Track(new GameObject("B"));
            GameObject c = Track(new GameObject("C"));
            b.transform.SetParent(a.transform);
            c.transform.SetParent(b.transform);
            var component = c.AddComponent<SaveComponent>();

            Assert.AreEqual("A/B/C", component.TransformPath(), "三层嵌套必须产出全路径（两级拼接会让 A/B/C 与 X/B/C 撞键）");
        }

        [Test]
        public void TransformPath_DifferentRootsSameTail_DoNotCollide()
        {
            GameObject x = Track(new GameObject("X"));
            GameObject a = Track(new GameObject("A"));
            GameObject b1 = Track(new GameObject("B"));
            GameObject b2 = Track(new GameObject("B"));
            GameObject c1 = Track(new GameObject("C"));
            GameObject c2 = Track(new GameObject("C"));
            b1.transform.SetParent(a.transform);
            b2.transform.SetParent(x.transform);
            c1.transform.SetParent(b1.transform);
            c2.transform.SetParent(b2.transform);

            string key1 = c1.AddComponent<SaveComponent>().TransformPath();
            string key2 = c2.AddComponent<SaveComponent>().TransformPath();

            Assert.AreNotEqual(key1, key2, "不同父链的同名尾段物体不得撞键");
        }

        [Test]
        public void TransformPath_SameNameSiblings_Disambiguated()
        {
            GameObject parent = Track(new GameObject("P"));
            GameObject first = Track(new GameObject("Dup"));
            GameObject second = Track(new GameObject("Dup"));
            first.transform.SetParent(parent.transform);
            second.transform.SetParent(parent.transform);

            string key1 = first.AddComponent<SaveComponent>().TransformPath();
            string key2 = second.AddComponent<SaveComponent>().TransformPath();

            Assert.AreNotEqual(key1, key2, "同名兄弟必须经序号消歧");
            StringAssert.Contains("Dup", key1);
            StringAssert.Contains("Dup", key2);
        }

        [Test]
        public void TransformPath_UniqueName_KeepsBareName()
        {
            GameObject parent = Track(new GameObject("P"));
            GameObject child = Track(new GameObject("Only"));
            child.transform.SetParent(parent.transform);

            Assert.AreEqual("P/Only", child.AddComponent<SaveComponent>().TransformPath());
        }

        [Test]
        public void TransformPath_SameNameSceneRoots_Disambiguated()
        {
            GameObject root1 = Track(new GameObject("Root"));
            GameObject root2 = Track(new GameObject("Root"));
            GameObject child1 = Track(new GameObject("Leaf"));
            GameObject child2 = Track(new GameObject("Leaf"));
            child1.transform.SetParent(root1.transform);
            child2.transform.SetParent(root2.transform);

            string key1 = child1.AddComponent<SaveComponent>().TransformPath();
            string key2 = child2.AddComponent<SaveComponent>().TransformPath();

            Assert.AreNotEqual(key1, key2, "同名场景根的同名子物体不得撞键");
        }

        /// <summary>
        /// 登记测试物体供 TearDown 销毁。
        /// </summary>
        private GameObject Track(GameObject go)
        {
            _objects.Add(go);
            return go;
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

        #endregion

        #region 文件头标志位 [HEADER FLAGS]

        [Test]
        public void TryLoad_UnknownHeaderFlags_ReturnsUnsupportedVersion()
        {
            var paths = SaveServiceHandler.ResolveSavePaths("slot", TestFolder);
            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 1, PlayerName = "f" }, ESaveBackend.Json, 1, CancellationToken.None);

            // 篡改 flags 为未知位（模拟未来版本运行时写出的档）
            byte[] fileBytes = File.ReadAllBytes(paths.SaveFilePath);
            fileBytes[28] = 0x40;
            // flags 段在校验载荷长度/CRC 之前求值，但篡改 flags 不改载荷——为隔离变量，同步重算载荷 CRC 保持自洽
            uint patchedCrc = Crc32.Compute(fileBytes.AsSpan(SaveFileHeader.Size));
            fileBytes[20] = (byte)patchedCrc;
            fileBytes[21] = (byte)(patchedCrc >> 8);
            fileBytes[22] = (byte)(patchedCrc >> 16);
            fileBytes[23] = (byte)(patchedCrc >> 24);
            File.WriteAllBytes(paths.SaveFilePath, fileBytes);

            ExpectErrorLogForUtf();
            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out _);
            Assert.AreEqual(SaveError.UnsupportedVersion, error, "未知标志位必须拒载（旧运行时静默忽略会写坏档）");
            AssertErrorLogged("UnsupportedVersion");
        }

        [Test]
        public void TryLoad_KnownFlagsZero_StillLoads()
        {
            var paths = SaveServiceHandler.ResolveSavePaths("slot", TestFolder);
            _handler.SaveBlockCore(paths, SaveServiceHandler.MAIN_BLOCK_KEY, new SaveData { Gold = 7, PlayerName = "ok" }, ESaveBackend.Json, 1, CancellationToken.None);

            SaveError error = _handler.TryLoadBlockCore<SaveData>(paths, SaveServiceHandler.MAIN_BLOCK_KEY, out SaveData loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(7, loaded.Gold);
        }

        #endregion

        #region 场景命名空间 [SCENE NAMESPACE]

        [Test]
        public void ResolveSceneNamespace_UntitledActiveScene_FallsBackToName()
        {
            // EditMode 测试活跃场景为未保存场景（无资产路径）——命名空间回退场景名
            UnityEngine.SceneManagement.Scene active = SceneManager.GetActiveScene();
            Assert.AreEqual(string.Empty, active.path, "测试活跃场景应为未保存场景");
            Assert.AreEqual(active.name ?? string.Empty, SaveComponent.ResolveSceneNamespace(active), "未保存场景命名空间回退场景名");
        }

        [Test]
        public void ResolveSceneNamespace_DefaultScene_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, SaveComponent.ResolveSceneNamespace(default), "非法/未初始化场景句柄不得抛异常");
        }

        [Test]
        public void ResolvedBlockKey_SavedScene_UsesScenePathNamespace()
        {
            // 新建空场景（成为活跃场景）→ 保存为资产（盖章场景资产路径）——EditMode 下 NewScene(Additive) 要求
            // 活跃场景已保存，本流程规避该前置条件；收尾恢复未保存空场景，消除夹具内测试顺序耦合
            string scenePath = "Assets/save-ns-test-scene.unity";
            UnityEngine.SceneManagement.Scene created = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                EditorSceneManager.SaveScene(created, scenePath);
                Assert.AreEqual(scenePath, SaveComponent.ResolveSceneNamespace(created), "已保存场景命名空间取资产路径");

                var go = Track(new GameObject("SamePath"));
                var component = go.AddComponent<SaveComponent>();
                component.EnsureActivated();
                try
                {
                    StringAssert.StartsWith(scenePath + ":", component.ResolvedBlockKey,
                        "已保存场景的块键必须以资产路径为命名空间（同名 Additive 场景防撞键）");
                }
                finally
                {
                    SaveComponentRegistry.Unregister(component);
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset(scenePath);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        #endregion

        #region 并发门 [FILE GATE]

        [Test]
        public void ConcurrentWrites_ToSameFile_DoNotLoseBlocks()
        {
            // 主线程预热：Settings 懒加载 / 序列化器注册表静态构造 均须先于工作线程访问
            _handler.SaveBlock(new SaveData { Gold = 0, PlayerName = "warmup" }, "warmup", "warmup-key", TestFolder);
            _handler.DeleteSave("warmup", TestFolder);

            const int writerCount = 4;
            const int writesPerWriter = 8;
            var threads = new Thread[writerCount];
            var failures = new Exception[writerCount];

            for (int w = 0; w < writerCount; w++)
            {
                int writerIndex = w;
                threads[w] = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < writesPerWriter; i++)
                        {
                            _handler.SaveBlock(
                                new SaveData { Gold = i, PlayerName = "w" + writerIndex },
                                "slot", "key-" + writerIndex + "-" + i, TestFolder);
                        }
                    }
                    catch (Exception exception)
                    {
                        failures[writerIndex] = exception;
                    }
                });
            }

            for (int w = 0; w < writerCount; w++)
            {
                threads[w].Start();
            }

            for (int w = 0; w < writerCount; w++)
            {
                threads[w].Join();
                Assert.IsNull(failures[w], $"写入线程 {w} 不应失败: {failures[w]?.Message}");
            }

            SaveBlockInfo[] infos = _handler.GetBlockInfos("slot", TestFolder);
            Assert.AreEqual(writerCount * writesPerWriter, infos.Length, "并发读-改-写不得丢块（串行门契约）");

            for (int w = 0; w < writerCount; w++)
            {
                for (int i = 0; i < writesPerWriter; i++)
                {
                    SaveError error = _handler.TryLoadBlockCore<SaveData>(
                        SaveServiceHandler.ResolveSavePaths("slot", TestFolder), "key-" + w + "-" + i, out SaveData loaded);
                    Assert.AreEqual(SaveError.None, error, $"key-{w}-{i} 应可加载");
                    Assert.AreEqual(i, loaded.Gold, $"key-{w}-{i} 内容应完整");
                }
            }
        }

        [Test]
        public void FileGate_ReleasesEntry_AfterLastLeave()
        {
            var paths = SaveServiceHandler.ResolveSavePaths("gate-reclaim-slot", TestFolder);
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            gate.Wait();
            SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: true);

            IDictionary table = GateTable();
            Assert.IsFalse(table.Contains(paths.SaveFilePath), "最后一个占用者离开后表项应惰性回收");
        }

        [Test]
        public void FileGate_CancelledWaiter_DoesNotLeakEntry()
        {
            var paths = SaveServiceHandler.ResolveSavePaths("gate-cancel-slot", TestFolder);
            // Enter 后未持门（acquired: false 模拟等门期取消）——仅归还占用不 Release
            SemaphoreSlim gate = SaveFileGate.Enter(paths.SaveFilePath);
            SaveFileGate.Leave(paths.SaveFilePath, gate, acquired: false);

            Assert.IsFalse(GateTable().Contains(paths.SaveFilePath), "等门期取消不得泄漏门表项");
        }

        [Test]
        public void FileGate_ConcurrentHolders_KeepEntryAlive()
        {
            var paths = SaveServiceHandler.ResolveSavePaths("gate-alive-slot", TestFolder);
            SemaphoreSlim gate1 = SaveFileGate.Enter(paths.SaveFilePath);
            SemaphoreSlim gate2 = SaveFileGate.Enter(paths.SaveFilePath);
            Assert.AreSame(gate1, gate2, "同路径必须取到同一把门");

            gate1.Wait();
            SaveFileGate.Leave(paths.SaveFilePath, gate1, acquired: true);
            Assert.IsTrue(GateTable().Contains(paths.SaveFilePath), "仍有占用者时表项不得回收");

            SaveFileGate.Leave(paths.SaveFilePath, gate2, acquired: false);
            Assert.IsFalse(GateTable().Contains(paths.SaveFilePath));
        }

        [Test]
        public void DeleteSave_WhileFileGateHeld_BlocksUntilRelease()
        {
            // 主线程预热（Settings 懒加载须先于工作线程访问）并造档
            _handler.SaveBlock(new SaveData { Gold = 1, PlayerName = "p" }, "gate-del-slot", "k", TestFolder);
            Assert.IsTrue(_handler.FileExists("gate-del-slot", TestFolder));

            // 持文件级门——整档删除（分层门：根+文件夹+文件）必须在文件级排队
            var paths = SaveServiceHandler.ResolveSavePaths("gate-del-slot", TestFolder);
            SemaphoreSlim fileGate = SaveFileGate.Enter(paths.SaveFilePath);
            fileGate.Wait();

            Exception failure = null;
            bool completed = false;
            var thread = new Thread(() =>
            {
                try
                {
                    _handler.DeleteSave("gate-del-slot", TestFolder);
                    completed = true;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.Start();
            Thread.Sleep(200);
            Assert.IsFalse(completed, "文件级门被持期间整档删除必须排队（防删除后并发写入复活文件）");

            SaveFileGate.Leave(paths.SaveFilePath, fileGate, acquired: true);
            thread.Join();
            Assert.IsNull(failure, failure?.Message);
            Assert.IsTrue(completed);
            Assert.IsFalse(_handler.FileExists("gate-del-slot", TestFolder), "门释放后删除应完成");
        }

        [Test]
        public void DeleteSaveFolder_WhileFolderGateHeld_BlocksUntilRelease()
        {
            _handler.SaveBlock(new SaveData { Gold = 1, PlayerName = "p" }, "gate-folder-slot", "k", TestFolder);
            var paths = SaveServiceHandler.ResolveSavePaths("gate-folder-slot", TestFolder);

            // 持文件夹级门（块级读写持「根+文件夹+文件」三级，故文件夹删除与任一槽位写入互斥）
            SemaphoreSlim dirGate = SaveFileGate.Enter(paths.DirectoryPath);
            dirGate.Wait();

            Exception failure = null;
            bool completed = false;
            var thread = new Thread(() =>
            {
                try
                {
                    _handler.DeleteSaveFolder(TestFolder);
                    completed = true;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.Start();
            Thread.Sleep(200);
            Assert.IsFalse(completed, "文件夹级门被持期间文件夹删除必须排队");

            SaveFileGate.Leave(paths.DirectoryPath, dirGate, acquired: true);
            thread.Join();
            Assert.IsNull(failure, failure?.Message);
            Assert.IsTrue(completed);
            Assert.IsFalse(_handler.FileExists("gate-folder-slot", TestFolder), "门释放后文件夹删除应完成");
        }

        [Test]
        public void DeleteAllSaveFiles_WhileRootGateHeld_BlocksUntilRelease()
        {
            _handler.SaveBlock(new SaveData { Gold = 1, PlayerName = "p" }, "gate-root-slot", "k", TestFolder);
            var paths = SaveServiceHandler.ResolveSavePaths("gate-root-slot", TestFolder);

            // 持根级门（与 BuildDataRootDirectory 同串：文件夹路径去尾分隔符后取父目录）
            string rootKey = Path.GetDirectoryName(paths.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar));
            SemaphoreSlim rootGate = SaveFileGate.Enter(rootKey);
            rootGate.Wait();

            Exception failure = null;
            bool completed = false;
            var thread = new Thread(() =>
            {
                try
                {
                    _handler.DeleteAllSaveFiles();
                    completed = true;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.Start();
            Thread.Sleep(200);
            Assert.IsFalse(completed, "根级门被持期间根目录清空必须排队（用户数据清除可靠性）");

            SaveFileGate.Leave(rootKey, rootGate, acquired: true);
            thread.Join();
            Assert.IsNull(failure, failure?.Message);
            Assert.IsTrue(completed);
            Assert.IsFalse(_handler.FileExists("gate-root-slot", TestFolder), "门释放后根目录清空应完成");
        }

        /// <summary>
        /// 反射读取门表（惰性回收断言用）。
        /// </summary>
        private static IDictionary GateTable()
        {
            var field = typeof(SaveFileGate).GetField("s_Gates", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "SaveFileGate.s_Gates 字段应存在");
            return (IDictionary)field.GetValue(null);
        }

        #endregion
    }
}
