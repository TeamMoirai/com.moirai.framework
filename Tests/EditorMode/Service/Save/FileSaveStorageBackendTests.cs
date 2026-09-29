using System;
using System.IO;
using System.Threading;
using Moirai.Atropos;
using Moirai.Atropos.Save;
using NUnit.Framework;

namespace Service.Save
{
    /// <summary>
    /// <see cref="FileSaveStorageBackend"/> 存储层契约测试：原子写入与往返、删除幂等（连带清中转日志）、槽位枚举（扩展名精确过滤 + 倒序）、单档备份/恢复、回退替换与中断恢复、能力自描述与设置默认值。
    /// </summary>
    /// <remarks>
    /// 全流程真实文件 IO（临时目录隔离）；存储层为无状态纯 .NET 实现，直接实例化测试。
    /// </remarks>
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

        // 回退/恢复用例的三种内容，彼此可区分：旧存档、新存档、项目侧单槽备份
        private static readonly byte[] s_OldBytes = { 0x4F, 0x4C, 0x44, 0x01 };
        private static readonly byte[] s_NewBytes = { 0x4E, 0x45, 0x57, 0x02 };
        private static readonly byte[] s_BackupBytes = { 0x42, 0x4B, 0x50, 0x03 };

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
        public void WriteAtomic_TwoSegment_RoundTripsInOrder()
        {
            string filePath = FilePath("slot-two-segment.sav");
            byte[] head = { 0x4D, 0x52, 0x53, 0x41 };
            byte[] payload = { 0x01, 0x02, 0x03, 0x04, 0x05 };

            _backend.WriteAtomic(filePath, head.AsSpan(), payload.AsSpan(), CancellationToken.None);

            SaveError error = _backend.TryReadAllBytes(filePath, out byte[] loaded);
            Assert.AreEqual(SaveError.None, error);
            var expected = new byte[head.Length + payload.Length];
            head.CopyTo(expected, 0);
            payload.CopyTo(expected, head.Length);
            Assert.AreEqual(expected, loaded, "两段写必须按「头 → 载荷」顺序落盘");
        }

        [Test]
        public void WriteAtomic_TwoSegment_EmptyHead_WritesPayloadOnly()
        {
            string filePath = FilePath("slot-empty-head.sav");
            byte[] payload = { 0x09, 0x08 };

            _backend.WriteAtomic(filePath, ReadOnlySpan<byte>.Empty, payload.AsSpan(), CancellationToken.None);

            SaveError error = _backend.TryReadAllBytes(filePath, out byte[] loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(payload, loaded);
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
        public void WriteAtomic_Stream_WritesAllContent_AndSupportsSeekPatching()
        {
            string filePath = FilePath("slot-stream.sav");
            byte[] payload = { 0x10, 0x20, 0x30, 0x40, 0x50 };

            _backend.WriteAtomic(filePath, stream =>
            {
                Assert.IsTrue(stream.CanSeek, "流式写委托收到的流必须可寻址（头补写场景依赖）");
                // 占位头 → 写载荷 → 回填真头（流式容器管线的 CRC 时序模式）
                stream.Write(new byte[4], 0, 4);
                stream.Write(payload, 0, payload.Length);
                stream.Position = 0;
                stream.Write(new byte[] { 0x4D, 0x52, 0x53, 0x41 }, 0, 4);
            }, CancellationToken.None);

            SaveError error = _backend.TryReadAllBytes(filePath, out byte[] loaded);
            Assert.AreEqual(SaveError.None, error);
            var expected = new byte[9];
            expected[0] = 0x4D; expected[1] = 0x52; expected[2] = 0x53; expected[3] = 0x41;
            payload.CopyTo(expected, 4);
            Assert.AreEqual(expected, loaded, "流式写应原样提交委托写入的全部内容（含回填头）");
        }

        [Test]
        public void WriteAtomic_Stream_DelegateThrows_CleansTempFile()
        {
            string filePath = FilePath("slot-stream-fail.sav");

            GameException caught = Assert.Throws<GameException>(() => _backend.WriteAtomic(filePath, stream =>
            {
                stream.WriteByte(0x01);
                throw new InvalidOperationException("delegate failure (injected)");
            }, CancellationToken.None));

            StringAssert.Contains(filePath, caught.Message, "异常应携带路径上下文");
            Assert.IsFalse(File.Exists(filePath), "失败后不应产生目标文件");
            string[] tempFiles = Directory.GetFiles(_rootPath, "*.tmp-*", SearchOption.AllDirectories);
            Assert.IsEmpty(tempFiles, "失败后不应残留临时文件");
        }

        [Test]
        public void WriteAtomic_Stream_NullDelegate_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _backend.WriteAtomic(FilePath("slot.sav"), (Action<Stream>)null, CancellationToken.None));
        }

        [Test]
        public void TryOpenRead_RoundTrips_AndMissingReturnsFileNotFound()
        {
            string filePath = FilePath("slot-open.sav");
            byte[] bytes = { 0x01, 0x02, 0x03, 0x04 };
            _backend.WriteAtomic(filePath, bytes, CancellationToken.None);

            SaveError error = _backend.TryOpenRead(filePath, out Stream stream);
            Assert.AreEqual(SaveError.None, error);
            Assert.IsNotNull(stream);
            using (stream)
            {
                var loaded = new byte[bytes.Length];
                int readTotal = 0;
                while (readTotal < loaded.Length)
                {
                    int read = stream.Read(loaded, readTotal, loaded.Length - readTotal);
                    if (read == 0)
                    {
                        break;
                    }

                    readTotal += read;
                }

                Assert.AreEqual(bytes.Length, readTotal, "流式读应返回全部字节");
                Assert.AreEqual(bytes, loaded);
            }

            SaveError missingError = _backend.TryOpenRead(FilePath("missing.sav"), out Stream missingStream);
            Assert.AreEqual(SaveError.FileNotFound, missingError, "缺档应判别为 FileNotFound");
            Assert.IsNull(missingStream);
        }

        [Test]
        public void TryReadAllBytes_Missing_ReturnsFileNotFound()
        {
            SaveError error = _backend.TryReadAllBytes(FilePath("missing.sav"), out byte[] bytes);

            Assert.AreEqual(SaveError.FileNotFound, error, "缺档应判别为 FileNotFound");
            Assert.IsNull(bytes);
        }

        [Test]
        public void TryGetWriteTimeUtc_Existing_ReturnsTrue_AndMatchesFileSystem()
        {
            string filePath = FilePath("slot.sav");
            _backend.WriteAtomic(filePath, new byte[] { 0x01 }, CancellationToken.None);
            DateTime expected = File.GetLastWriteTimeUtc(filePath);

            bool found = _backend.TryGetWriteTimeUtc(filePath, out DateTime writeTimeUtc);

            Assert.IsTrue(found, "已存在文件应查询成功");
            Assert.AreEqual(expected, writeTimeUtc, "写入时间应与文件系统元数据一致");
        }

        [Test]
        public void TryGetWriteTimeUtc_Missing_ReturnsFalse()
        {
            bool found = _backend.TryGetWriteTimeUtc(FilePath("missing.sav"), out DateTime writeTimeUtc);

            Assert.IsFalse(found, "缺档应返回 false");
            Assert.AreEqual(default(DateTime), writeTimeUtc);
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

        #region 回退替换与中断恢复 [FALLBACK / RECOVERY]

        [Test]
        public void FallbackReplace_ExistingTarget_ReplacesInPlaceAndLeavesNoJournal()
        {
            string filePath = FilePath("fallback-ok.sav");
            string tempPath = filePath + FileSaveStorageBackend.TEMP_FILE_SUFFIX + "t1";
            File.WriteAllBytes(filePath, s_OldBytes);
            File.WriteAllBytes(tempPath, s_NewBytes);

            FileSaveStorageBackend.FallbackReplace(tempPath, filePath);

            CollectionAssert.AreEqual(s_NewBytes, File.ReadAllBytes(filePath), "新内容必须到位");
            Assert.IsFalse(File.Exists(tempPath), "临时文件不得残留");
            Assert.IsFalse(File.Exists(filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX), "替换成功后不得残留日志文件");
        }

        [Test]
        public void FallbackReplace_MissingTarget_MovesTempIntoPlace()
        {
            string filePath = FilePath("fallback-new.sav");
            string tempPath = filePath + FileSaveStorageBackend.TEMP_FILE_SUFFIX + "t2";
            File.WriteAllBytes(tempPath, s_NewBytes);

            FileSaveStorageBackend.FallbackReplace(tempPath, filePath);

            CollectionAssert.AreEqual(s_NewBytes, File.ReadAllBytes(filePath), "目标原本不存在时直接改名到位");
            Assert.IsFalse(File.Exists(filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX), "没有旧档就无需日志文件");
        }

        [Test]
        public void FallbackReplace_SecondStepFails_RestoresOldContentInPlace()
        {
            string filePath = FilePath("fallback-rollback.sav");
            // 临时文件缺失 → 改名到位那一步必然失败，这一步要演的是「搬走旧档之后才崩」
            string missingTemp = FilePath("never-written.sav" + FileSaveStorageBackend.TEMP_FILE_SUFFIX + "t3");
            File.WriteAllBytes(filePath, s_OldBytes);

            Assert.Catch<IOException>(() => FileSaveStorageBackend.FallbackReplace(missingTemp, filePath),
                "前置条件：改名到位那一步必须真的抛错，否则这一格什么都没测");

            Assert.IsTrue(File.Exists(filePath), "失败后主档位置必须仍有一可读文件，不得只留在日志里");
            CollectionAssert.AreEqual(s_OldBytes, File.ReadAllBytes(filePath), "回滚后旧存档必须原样可读");
            Assert.IsFalse(File.Exists(filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX), "回滚后日志文件应已让位给主档");
        }

        [Test]
        public void FallbackReplace_DoesNotDisturbSingleSlotBackup()
        {
            string filePath = FilePath("fallback-backup-safe.sav");
            string tempPath = filePath + FileSaveStorageBackend.TEMP_FILE_SUFFIX + "t4";
            string backupPath = filePath + ".bak";
            File.WriteAllBytes(filePath, s_OldBytes);
            File.WriteAllBytes(backupPath, s_BackupBytes);
            File.WriteAllBytes(tempPath, s_NewBytes);

            FileSaveStorageBackend.FallbackReplace(tempPath, filePath);

            // .bak 是项目侧 CreateBackup/RestoreBackup 的单槽位；回退若借它中转，玩家手动恢复会捞到写入中途的快照
            Assert.IsTrue(File.Exists(backupPath),
                "回退替换不得吃掉项目侧的单槽备份位（借 .bak 中转即为污染）");
            CollectionAssert.AreEqual(s_BackupBytes, File.ReadAllBytes(backupPath),
                "回退替换不得改写单槽备份位的内容");
        }

        [Test]
        public void RecoverInterruptedWrites_PrimaryMissingWithJournal_RestoresOldSave()
        {
            string filePath = FilePath("recover-crash.sav");
            File.WriteAllBytes(filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX, s_OldBytes);

            _backend.RecoverInterruptedWrites(_rootPath);

            Assert.IsTrue(File.Exists(filePath), "主档缺失而日志档在时应恢复回主档");
            CollectionAssert.AreEqual(s_OldBytes, File.ReadAllBytes(filePath), "恢复回来的必须是崩溃前的旧存档");
            Assert.IsFalse(File.Exists(filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX), "恢复后日志档应让位给主档");
        }

        [Test]
        public void RecoverInterruptedWrites_PrimaryPresent_ClearsStaleJournal()
        {
            string filePath = FilePath("recover-complete.sav");
            File.WriteAllBytes(filePath, s_NewBytes);
            File.WriteAllBytes(filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX, s_OldBytes);

            _backend.RecoverInterruptedWrites(_rootPath);

            CollectionAssert.AreEqual(s_NewBytes, File.ReadAllBytes(filePath), "主档已到位时恢复不得覆盖成新写的存档");
            Assert.IsFalse(File.Exists(filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX), "成功写入后残留的日志属陈旧，应清掉");
        }

        [Test]
        public void RecoverInterruptedWrites_UnrelatedFiles_AreLeftAlone()
        {
            string otherFilePath = FilePath("untouched.sav");
            string otherTemp = FilePath("stray.tmp-abc");
            File.WriteAllBytes(otherFilePath, s_OldBytes);
            File.WriteAllBytes(otherTemp, s_NewBytes);

            _backend.RecoverInterruptedWrites(_rootPath);

            CollectionAssert.AreEqual(s_OldBytes, File.ReadAllBytes(otherFilePath), "无日志档的存档不得被动到");
            Assert.IsTrue(File.Exists(otherTemp), "孤儿临时文件归 CleanupOrphanTempFiles 管，恢复这一步不该顺手删它");
        }

        [Test]
        public void RecoverInterruptedWrites_EmptyPrimaryWithJournal_KeepsJournal()
        {
            string filePath = FilePath("recover-remnant.sav");
            string journalPath = filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX;
            File.WriteAllBytes(filePath, Array.Empty<byte>()); // 删不掉的空残迹，不是已提交的存档
            File.WriteAllBytes(journalPath, s_OldBytes);

            _backend.RecoverInterruptedWrites(_rootPath);

            Assert.IsTrue(File.Exists(journalPath), "主档只是空残迹时不得把 journal 当陈旧清掉——它是唯一可读副本");
            CollectionAssert.AreEqual(s_OldBytes, File.ReadAllBytes(journalPath), "保留下来的 journal 必须仍是崩溃前的旧档");
        }

        [Test]
        public void RollbackJournal_MissingJournal_LeavesPrimaryUntouched()
        {
            string filePath = FilePath("rollback-nojournal.sav");
            string journalPath = filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX;
            File.WriteAllBytes(filePath, s_NewBytes); // 并发恢复已把旧档抬回主档，journal 因此为空

            FileSaveStorageBackend.RollbackJournal(journalPath, filePath);

            Assert.IsTrue(File.Exists(filePath), "journal 不在时回滚一律不动主档");
            CollectionAssert.AreEqual(s_NewBytes, File.ReadAllBytes(filePath), "主档上那份可读存档不得被回滚删掉");
        }

        [Test]
        public void RollbackJournal_JournalPresent_RestoresOldContent()
        {
            string filePath = FilePath("rollback-ok.sav");
            string journalPath = filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX;
            File.WriteAllBytes(journalPath, s_OldBytes);

            FileSaveStorageBackend.RollbackJournal(journalPath, filePath);

            CollectionAssert.AreEqual(s_OldBytes, File.ReadAllBytes(filePath), "抬回后主档位置必须是旧档");
            Assert.IsFalse(File.Exists(journalPath), "抬回成功后 journal 应让位");
        }

        [Test]
        public void RollbackJournal_RestoreFails_KeepsJournalAsReadableCopy()
        {
            // 失败注入能力探测：本用例靠「独占句柄挡住删/改名」制造回滚失败。Windows 的强制文件锁下
            // FileShare.None 真挡得住；POSIX（macOS/Linux）上打开句柄不阻止 unlink/rename，回滚会直接
            // 成功、journal 正常让位，注入天然不成立。探测不到该能力时本格无从验证「失败保 journal」。
            // 恢复条件：在 Windows 或支持强制文件锁的文件系统上运行。
            string probePath = FilePath("lock-capability-probe");
            File.WriteAllBytes(probePath, Array.Empty<byte>());
            bool lockBlocksDeletion;
            try
            {
                using (File.Open(probePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    try
                    {
                        File.Delete(probePath);
                        lockBlocksDeletion = false;
                    }
                    catch (Exception)
                    {
                        lockBlocksDeletion = true;
                    }
                }
            }
            finally
            {
                if (File.Exists(probePath)) File.Delete(probePath);
            }

            if (!lockBlocksDeletion)
            {
                Assert.Ignore("当前文件系统上打开句柄不阻止删除/改名（POSIX 语义），回滚失败注入不成立；恢复条件：Windows 或支持强制文件锁的文件系统");
            }

            string filePath = FilePath("rollback-blocked.sav");
            string journalPath = filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX;
            File.WriteAllBytes(filePath, s_NewBytes);
            File.WriteAllBytes(journalPath, s_OldBytes);

            // 主档被句柄占住：删不掉、也不能改名覆盖，回滚两头都失败
            using (File.Open(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                Assert.DoesNotThrow(() => FileSaveStorageBackend.RollbackJournal(journalPath, filePath),
                    "回滚自身失败不得再抛新异常盖掉原异常");
            }

            Assert.IsTrue(File.Exists(journalPath), "抬不回去时必须留着 journal，不能落得两头皆空");
            CollectionAssert.AreEqual(s_OldBytes, File.ReadAllBytes(journalPath), "保留的 journal 内容必须还是旧档");
        }

        [Test]
        public void DeleteFile_AlsoClearsJournal_SoInterruptedSaveCannotResurrect()
        {
            string filePath = FilePath("delete-journal.sav");
            string journalPath = filePath + FileSaveStorageBackend.JOURNAL_FILE_SUFFIX;
            File.WriteAllBytes(filePath, s_NewBytes);
            File.WriteAllBytes(journalPath, s_OldBytes);

            _backend.DeleteFile(filePath);

            Assert.IsFalse(File.Exists(filePath), "主档应被删除");
            Assert.IsFalse(File.Exists(journalPath), "中转日志必须连带清掉，否则下次恢复会把删掉的旧档抬回来");

            _backend.RecoverInterruptedWrites(_rootPath);
            Assert.IsFalse(File.Exists(filePath), "删档后不得被 RecoverInterruptedWrites 复活");
        }

        #endregion

        #region 能力与配置 [CAPABILITIES / SETTINGS]

        [Test]
        public void Capabilities_DeclaresLocalFileSemantics()
        {
            SaveStorageCapabilities capabilities = _backend.Capabilities;
            Assert.IsTrue(capabilities.SupportsAtomicRename, "本地文件后端应保证无半写窗口且中断后旧档可恢复（判据见 SaveStorageCapabilities.SupportsAtomicRename）");
            Assert.IsFalse(capabilities.VolatileStorage, "本地文件后端不应声明易失存储");
            Assert.IsTrue(capabilities.SyncReadsAuthoritative, "本地文件后端同步读应权威（同步裸名读即权威数据）");
        }

        [Test]
        public void Handler_StorageBackend_DefaultsToFileBackend()
        {
            // 存储后端配置内聚于处理器——既有配置资产未序列化该字段时，字段初始化器兜出文件后端默认值（无需资产迁移）
            Assert.IsNotNull(SaveServiceSettings.SaveServiceHandler, "设置应配置存档处理器");
            Assert.IsInstanceOf<FileSaveStorageBackend>(SaveServiceSettings.SaveServiceHandler.StorageBackend);
        }

        [Test]
        public void Default_SharedInstance_IsUsable()
        {
            string filePath = FilePath("shared.sav");
            FileSaveStorageBackend.s_Default.WriteAtomic(filePath, new byte[] { 0x05 }, CancellationToken.None);

            SaveError error = FileSaveStorageBackend.s_Default.TryReadAllBytes(filePath, out byte[] loaded);
            Assert.AreEqual(SaveError.None, error);
            Assert.AreEqual(new byte[] { 0x05 }, loaded);
        }

        #endregion
    }
}
