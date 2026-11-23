using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Service.Save
{
    /// <summary>
    /// 门面能力协商判据（v3 治病版 S0）：门面能力访问必须经契约协商而非家族基座强转。
    /// </summary>
    /// <remarks>
    /// 三条判据：
    /// ① 非家族后端（实现契约但不实现 ISaveServiceBackend 能力契约）调用门面能力 API 时 fail-fast 带迁移指引，
    ///    不是 <see cref="InvalidCastException"/>——红在真实缺陷（强转）上；
    /// ② 家族后端（派生 SaveServiceHandler）同 API 正常往返——能力协商绿路回归护栏；
    /// ③ 读路径 peek 站点（实体/组件恢复）对能力缺失后端降级不抛，语义等价于未就绪降级。
    /// 判据经生成器发射的静态槽接缝 <c>SaveService.Internal_UseHandler</c> 注入，不触容器。
    /// 执行模型遵循 RestCloudSaveKvStoreTests 契约：EditMode 无 SyncContext，UniTask 经 AsTask + WaitForTask 协程。
    /// </remarks>
    public class SaveFacadeCapabilityTests
    {
        private string _rootPath;

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-facade-capability-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
            SaveServiceHandler.s_OverrideBasePath = _rootPath;
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.Internal_UseHandler(null);
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
                // 临时目录删除失败不影响判定；Windows 文件句柄释放滞后时留给系统清理
            }
        }

        /// <summary>契约实现但不派生家族基座的最小后端：28 个契约成员全部抛桩——断言点在能力协商，先于任何契约成员分发。</summary>
        private sealed class BareSaveBackend : ServiceBase, ISaveService
        {
            public void CreateBackup(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public void DeleteAllSaveFiles() => throw NotContract();
            public UniTask DeleteAllSaveFilesAsync(CancellationToken cancellationToken = default) => throw NotContract();
            public void DeleteBlock(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public UniTask DeleteBlockAsync(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public void DeleteSave(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public UniTask DeleteSaveAsync(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public void DeleteSaveFolder(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public UniTask DeleteSaveFolderAsync(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public string DetermineSavePath(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public bool FileExists(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public SaveBlockInfo[] GetBlockInfos(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public SaveFileInfo[] GetSaveFiles(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public UniTask<SaveFileInfo[]> GetSaveFilesAsync(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public SaveError MigrateSave(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public UniTask<SaveError> MigrateSaveAsync(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public void RestoreBackup(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public bool TryDeleteAllSaveFiles() => throw NotContract();
            public UniTask<bool> TryDeleteAllSaveFilesAsync(CancellationToken cancellationToken = default) => throw NotContract();
            public bool TryDeleteSave(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public UniTask<bool> TryDeleteSaveAsync(string fileName, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public bool TryDeleteSaveFolder(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public UniTask<bool> TryDeleteSaveFolderAsync(string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public UniTask SaveBlockAsync<T>(T data, string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, ushort backend = SaveBackendIds.JSON, int dataVersion = 1, CancellationToken cancellationToken = default) => throw NotContract();
            public UniTask<T> LoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public UniTask<SaveResult<T>> TryLoadBlockAsync<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken cancellationToken = default) => throw NotContract();
            public T LoadBlock<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();
            public SaveResult<T> TryLoadBlock<T>(string fileName, string key, string folderName = SaveServiceDefaults.DEFAULT_FOLDER_NAME) => throw NotContract();

            private static NotImplementedException NotContract() => new NotImplementedException("BareSaveBackend contract stub.");
        }

        /// <summary>判据往返用数据块。</summary>
        [Serializable]
        private class SaveData
        {
            public int Gold;
            public string PlayerName;
        }

        [Test]
        public void Facade_WhenBackendLacksCapability_FailsFastWithActionableMessage()
        {
            SaveService.Internal_UseHandler(new BareSaveBackend());

            try
            {
                // 扩展 SaveBlock（SaveServiceHandler.Blocks.cs:146 的重载）不在契约——正是 v2 立论的裂缝符号
                SaveService.SaveBlock(new SaveData { Gold = 1, PlayerName = "cap" }, "slot", SaveService.MAIN_BLOCK_KEY);
                Assert.Fail("Expected GameException from capability negotiation, but no exception was thrown.");
            }
            catch (GameException exception)
            {
                // fail-fast 且消息含能力契约名与迁移指引——不是 InvalidCastException
                StringAssert.Contains("ISaveServiceBackend", exception.Message);
                StringAssert.Contains("SaveServiceHandler", exception.Message);
            }
            catch (InvalidCastException exception)
            {
                Assert.Fail($"Capability negotiation leaked InvalidCastException (cast not eliminated): {exception.Message}");
            }
        }

        [Test]
        public void Facade_WhenBackendImplementsCapability_ResolvesInternalCapability()
        {
            // PlainSaveHandler 为 internal，测试程序集经 IVT（AssemblyInfo.cs:10）可见
            var handler = new PlainSaveHandler();
            SaveService.Internal_UseHandler(handler);

            var data = new SaveData { Gold = 4321, PlayerName = "Moirai" };
            SaveService.SaveBlock(data, "slot", SaveService.MAIN_BLOCK_KEY);

            SaveData loaded = SaveService.LoadBlock<SaveData>("slot", SaveService.MAIN_BLOCK_KEY);
            Assert.AreEqual(4321, loaded.Gold);
            Assert.AreEqual("Moirai", loaded.PlayerName);
        }

        [UnityTest]
        public IEnumerator Facade_WhenBackendLacksCapability_PeekSitesDegradeWithWarning()
        {
            SaveService.Internal_UseHandler(new BareSaveBackend());

            // 读路径 peek 站点（实体恢复走 ReadRawBlocksAsync 能力）：能力缺失降级不抛，等价未就绪语义
            Task restoreTask = SaveService.RestoreEntitiesAsync("slot", SaveServiceDefaults.DEFAULT_FOLDER_NAME, CancellationToken.None).AsTask();
            yield return WaitForTask(restoreTask);

            // LoadBlock 走契约成员（BareSaveBackend 桩 NotImplementedException）——降级测试只跑能力站点，不触契约分发
        }

        /// <summary>
        /// 主线程等待异步任务完成（逐帧 yield 保持编辑器泵——模式契约见 RestCloudSaveKvStoreTests）。
        /// </summary>
        private static IEnumerator WaitForTask(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            yield return null;

            if (task.IsFaulted && task.Exception != null)
            {
                throw task.Exception.InnerException ?? task.Exception;
            }
        }
    }
}
