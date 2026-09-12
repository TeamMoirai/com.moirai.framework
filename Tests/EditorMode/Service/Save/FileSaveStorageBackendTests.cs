using System;
using System.IO;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Save
{
    /// <summary>
    /// <see cref="FileSaveStorageBackend"/> 存储层契约测试（V3-P1 存储抽象下沉回归）：
    /// 原子写入与往返、删除幂等、槽位枚举（扩展名精确过滤 + 倒序）、单档备份/恢复、能力自描述与设置默认值。
    /// <para>全流程真实文件 IO（临时目录隔离）；存储层为无状态纯 .NET 实现，直接实例化测试。</para>
    /// </summary>
    public class FileSaveStorageBackendTests
    {
        private FileSaveStorageBackend _backend;
        private string _rootPath;

        [SetUp]
        public void SetUp()
        {
            _backend = new FileSaveStorageBackend();
            _rootPath = Path.Combine(Path.GetTempPath(), "moirai-save-storage-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootPath);
        }

        [TearDown]
        public void TearDown()
        {
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

        /// <summary>
        /// 在测试根目录下拼装目标文件路径。
        /// </summary>
        private string FilePath(string name)
        {
            return Path.Combine(_rootPath, name);
        }

        #region 原子写与读取 [WRITE / READ]

        [Test]
        public void WriteAtomic_ThenTryRead_RoundTrips()
        {
            string filePath = FilePath("slot.sav");
            byte[] bytes = { 0x4D, 0x52, 0x53, 0x41, 0x01, 0x02, 0x03 };

            _backend.WriteAtomic(filePath, bytes, CancellationToken.None);

            SaveError error = _backend.TryReadAllBytes(filePath, out byte[] loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(bytes, loaded, "写入字节应原样读回");
        }

        [Test]
        public void WriteAtomic_CreatesMissingDirectory()
        {
            string filePath = Path.Combine(_rootPath, "nested", "deep", "slot.sav");

            _backend.WriteAtomic(filePath, new byte[] { 0x01 }, CancellationToken.None);

            Assert.IsTrue(File.Exists(filePath), "目标目录不存在时应自动创建");
        }

        [Test]
        public void WriteAtomic_OverwritesExisting()
        {
            string filePath = FilePath("slot.sav");
            _backend.WriteAtomic(filePath, new byte[] { 0x01 }, CancellationToken.None);
            _backend.WriteAtomic(filePath, new byte[] { 0x02, 0x03 }, CancellationToken.None);

            SaveError error = _backend.TryReadAllBytes(filePath, out byte[] loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(new byte[] { 0x02, 0x03 }, loaded, "覆盖写入后应读到最新字节");
        }

        [Test]
        public void WriteAtomic_LeavesNoTempFiles()
        {
            string filePath = FilePath("slot.sav");
            _backend.WriteAtomic(filePath, new byte[] { 0x01 }, CancellationToken.None);
            _backend.WriteAtomic(filePath, new byte[] { 0x02 }, CancellationToken.None);

            string[] tempFiles = Directory.GetFiles(_rootPath, "*.tmp-*", SearchOption.AllDirectories);
            Assert.IsEmpty(tempFiles, "成功写入后不应残留临时文件");
        }

        [Test]
        public void TryReadAllBytes_Missing_ReturnsFileNotFound()
        {
            SaveError error = _backend.TryReadAllBytes(FilePath("missing.sav"), out byte[] bytes);

            Assert.AreEqual(SaveError.FileNotFound, error, "缺档应判别为 FileNotFound");
            Assert.IsNull(bytes);
        }

        #endregion

        #region 删除 [DELETE]

        [Test]
        public void DeleteFile_Idempotent()
        {
            string filePath = FilePath("slot.sav");
            Assert.DoesNotThrow(() => _backend.DeleteFile(filePath), "删除不存在文件应静默成功（幂等契约）");

            _backend.WriteAtomic(filePath, new byte[] { 0x01 }, CancellationToken.None);
            _backend.DeleteFile(filePath);
            Assert.IsFalse(File.Exists(filePath), "删除后文件应不存在");
        }

        [Test]
        public void DeleteDirectory_RemovesTree_AndIdempotent()
        {
            string nestedDirectory = Path.Combine(_rootPath, "slots");
            string nestedFile = Path.Combine(nestedDirectory, "deep", "slot.sav");
            _backend.WriteAtomic(nestedFile, new byte[] { 0x01 }, CancellationToken.None);

            _backend.DeleteDirectory(nestedDirectory);
            Assert.IsFalse(Directory.Exists(nestedDirectory), "删除目录应移除整个目录树");

            Assert.DoesNotThrow(() => _backend.DeleteDirectory(nestedDirectory), "删除不存在目录应静默成功（幂等契约）");
        }

        #endregion

        #region 枚举 [LISTING]

        [Test]
        public void EnumerateFiles_FiltersExactExtension_NewestFirst()
        {
            // Windows GetFiles 的 8.3 通配符怪癖：*.sav 会命中 *.saveall——必须按扩展名精确过滤
            string oldFile = FilePath("slot_old.sav");
            string newFile = FilePath("slot_new.sav");
            _backend.WriteAtomic(oldFile, new byte[] { 0x01 }, CancellationToken.None);
            _backend.WriteAtomic(newFile, new byte[] { 0x02 }, CancellationToken.None);
            File.WriteAllText(FilePath("decoy.saveall"), "x");

            File.SetLastWriteTimeUtc(oldFile, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newFile, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            SaveFileInfo[] files = _backend.EnumerateFiles(_rootPath, ".sav");
            Assert.AreEqual(2, files.Length, "同前缀不同扩展名的文件不应计入");
            Assert.AreEqual("slot_new", files[0].FileName, "应按最后写入时间倒序（最近优先）");
            Assert.AreEqual("slot_old", files[1].FileName);
            Assert.AreEqual(1L, files[1].SizeBytes, "元数据应携带文件大小");
            Assert.AreEqual(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), files[0].LastWriteTimeUtc);
        }

        [Test]
        public void EnumerateFiles_MissingDirectory_ReturnsEmpty()
        {
            SaveFileInfo[] files = _backend.EnumerateFiles(Path.Combine(_rootPath, "nonexistent"), ".sav");
            Assert.IsEmpty(files);
        }

        #endregion

        #region 备份与恢复 [BACKUP / RESTORE]

        [Test]
        public void CreateBackup_ThenRestoreBackup_RoundTrips()
        {
            string filePath = FilePath("slot.sav");
            _backend.WriteAtomic(filePath, new byte[] { 0x01, 0x02 }, CancellationToken.None);

            _backend.CreateBackup(filePath);
            Assert.IsTrue(File.Exists(filePath + ".bak"), "备份应落盘");

            _backend.WriteAtomic(filePath, new byte[] { 0x09 }, CancellationToken.None);
            _backend.RestoreBackup(filePath);

            SaveError error = _backend.TryReadAllBytes(filePath, out byte[] restored);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(new byte[] { 0x01, 0x02 }, restored, "恢复后应回到备份时点字节");
        }

        [Test]
        public void CreateBackup_MissingSource_Throws()
        {
            Assert.Throws<GameException>(() => _backend.CreateBackup(FilePath("missing.sav")));
        }

        [Test]
        public void RestoreBackup_MissingBackup_Throws()
        {
            Assert.Throws<GameException>(() => _backend.RestoreBackup(FilePath("no-backup.sav")));
        }

        #endregion

        #region 能力与配置 [CAPABILITIES / SETTINGS]

        [Test]
        public void Capabilities_DeclaresLocalFileSemantics()
        {
            SaveStorageCapabilities capabilities = _backend.Capabilities;
            Assert.IsTrue(capabilities.SupportsAtomicRename, "本地文件后端应声明原子改名能力");
            Assert.IsFalse(capabilities.VolatileStorage, "本地文件后端不应声明易失存储");
        }

        [Test]
        public void Settings_StorageBackend_DefaultsToFileBackend()
        {
            // 既有配置资产无 m_StorageBackend 字段——字段初始化器应兜出文件后端默认值（无需资产迁移）
            Assert.IsNotNull(SaveServiceSettings.StorageBackend, "存储后端应有默认值");
            Assert.IsInstanceOf<FileSaveStorageBackend>(SaveServiceSettings.StorageBackend);
        }

        [Test]
        public void Default_SharedInstance_IsUsable()
        {
            string filePath = FilePath("shared.sav");
            FileSaveStorageBackend.Default.WriteAtomic(filePath, new byte[] { 0x05 }, CancellationToken.None);

            SaveError error = FileSaveStorageBackend.Default.TryReadAllBytes(filePath, out byte[] loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(new byte[] { 0x05 }, loaded);
        }

        #endregion
    }
}
