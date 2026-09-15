using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档迁移管理器：迁移器注册表 + 文件级版本链解析与执行（迁移总线核心）。
    /// <para>版本模型：存档数据版本为 int 递增（0 = 版本化前的基线存档）；游戏层在启动期设置 <see cref="CurrentVersion"/> 为当前数据版本，
    /// 并为每个历史版本跃迁注册 <see cref="ISaveMigrator"/>（SaveHost SourceGenerator 扫描实现类经模块初始化器自注册，AOT 安全）。</para>
    /// <para>链契约：加载/写入管线触达版本低于 <see cref="CurrentVersion"/> 的存档时，沿 <c>FromVersion == 当前步版本</c> 的迁移器逐段升级到当前版本——
    /// 同一边多个迁移器按 <see cref="ISaveMigrator.Priority"/> 升序执行；同起始版本存在多条不同目标版本的边、链缺失或执行异常均为
    /// <see cref="SaveError.MigrationFailed"/> fail-fast；存档版本高于当前版本（降级）判别为 <see cref="SaveError.UnsupportedVersion"/>。</para>
    /// <para>采纳仪式：启用版本化（<see cref="CurrentVersion"/> 从 0 调大）且存在旧档时，须注册自版本 0 起的迁移链
    /// （旧档无元数据块按版本 0 处理；形状未变可用空迁移器桥接 0→1）。</para>
    /// <para>审计：每次迁移步向槽位元数据 <see cref="SaveMetadata.MigrationHistory"/> 追加一条
    /// <c>"{起始}->{目标}|{迁移器类型全名}|{UTC ISO-8601}"</c> 记录（随迁移回写持久化）。</para>
    /// <para>回写：迁移成功后由处理器按 <see cref="SaveServiceSettings.MigrationWriteBack"/> 惰性回写（默认开——避免每次加载重跑迁移链）；
    /// 同文件同会话的重复迁移经会话级缓存短路。注册表在启动期（主线程）写入、管线期（工作线程）只读快照。</para>
    /// </summary>
    public static class SaveMigrationManager
    {
        /// <summary>版本链步数防御上限（注册校验已保证 To &gt; From 严格递增，此为防环兜底）。</summary>
        internal const int MaxChainSteps = 1024;

        /// <summary>迁移器注册表（写入持锁；读取经 <see cref="SnapshotMigrators"/> 数组快照）。</summary>
        private static readonly List<ISaveMigrator> s_Migrators = new List<ISaveMigrator>();
        private static readonly object s_RegistryLock = new object();

        /// <summary>会话级已校验文件路径缓存（存档路径 → 标记时文件最后写入时间 UTC；版本相等或迁移完成的文件同会话不再重复探测/迁移——
        /// 文件写入时间变化（外部替换/云同步落盘/备份恢复）使缓存条目被动失效自动重探测；备份恢复与删除操作另经 <see cref="InvalidateSession"/> 主动失效）。</summary>
        private static readonly Dictionary<string, DateTime> s_MigratedPaths = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly object s_SessionLock = new object();

        /// <summary>当前数据版本（默认 0 = 版本化未激活，管线完全旁路迁移总线）。</summary>
        private static volatile int s_CurrentVersion;

        /// <summary>
        /// 当前存档数据版本（int 递增；游戏层在启动期主线程设置，默认 0 = 迁移总线未激活）。
        /// <para>写入管线在激活时把该版本盖章进槽位元数据块；读取管线对低于该版本的存档执行迁移链。</para>
        /// </summary>
        public static int CurrentVersion
        {
            get => s_CurrentVersion;
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Current save version must be >= 0.");
                }

                s_CurrentVersion = value;
            }
        }

        /// <summary>迁移总线是否激活（未激活时读写管线零开销旁路）。</summary>
        internal static bool IsActive => s_CurrentVersion > 0;

        #region 注册表 [REGISTRY]

        /// <summary>
        /// 注册迁移器（启动期主线程调用；同类型重复注册以最新为准——模块初始化器与手动注册并存场景幂等）。
        /// </summary>
        /// <param name="migrator">迁移器实例。</param>
        /// <exception cref="ArgumentNullException">迁移器为 null。</exception>
        /// <exception cref="ArgumentException">版本契约破坏（FromVersion &lt; 0 或 ToVersion &lt;= FromVersion——仅允许升级方向，天然杜绝版本环）。</exception>
        public static void Register(ISaveMigrator migrator)
        {
            if (migrator == null)
            {
                throw new ArgumentNullException(nameof(migrator));
            }

            if (migrator.FromVersion < 0 || migrator.ToVersion <= migrator.FromVersion)
            {
                throw new ArgumentException(StringUtility.Format(
                    "Save migrator '{0}' has an invalid version edge ({1} -> {2}): FromVersion must be >= 0 and ToVersion must be greater than FromVersion.",
                    migrator.GetType().FullName, migrator.FromVersion, migrator.ToVersion), nameof(migrator));
            }

            lock (s_RegistryLock)
            {
                Type migratorType = migrator.GetType();
                for (int i = s_Migrators.Count - 1; i >= 0; i--)
                {
                    if (s_Migrators[i].GetType() == migratorType)
                    {
                        s_Migrators.RemoveAt(i);
                    }
                }

                s_Migrators.Add(migrator);
            }
        }

        /// <summary>
        /// 已注册迁移器快照（数组拷贝；调试器/诊断用）。
        /// </summary>
        /// <returns>迁移器数组。</returns>
        public static ISaveMigrator[] Snapshot()
        {
            lock (s_RegistryLock)
            {
                return s_Migrators.ToArray();
            }
        }

        /// <summary>
        /// 取注册表内部快照（管线期只读）。
        /// </summary>
        private static ISaveMigrator[] SnapshotMigrators()
        {
            lock (s_RegistryLock)
            {
                return s_Migrators.ToArray();
            }
        }

        #endregion

        #region 会话缓存 [SESSION CACHE]

        /// <summary>
        /// 失效指定存档路径的会话迁移缓存（备份恢复/删除等改变文件内容的操作后调用）。
        /// </summary>
        /// <param name="saveFilePath">存档文件完整路径；null = 清空全部（目录级删除）。</param>
        internal static void InvalidateSession(string saveFilePath)
        {
            lock (s_SessionLock)
            {
                if (saveFilePath == null)
                {
                    s_MigratedPaths.Clear();
                }
                else
                {
                    s_MigratedPaths.Remove(saveFilePath);
                }
            }
        }

        /// <summary>
        /// 重置全部状态（测试钩子：清空注册表/会话缓存并复位当前版本）。
        /// </summary>
        internal static void ResetForTests()
        {
            lock (s_RegistryLock)
            {
                s_Migrators.Clear();
            }

            lock (s_SessionLock)
            {
                s_MigratedPaths.Clear();
            }

            s_CurrentVersion = 0;
        }

        /// <summary>
        /// 会话缓存命中判定（须在 <see cref="s_SessionLock"/> 内调用）：路径已标记且文件写入时间未变——
        /// 外部替换/云同步落盘改变写入时间时被动失效（重走版本探测）；读取失败按未命中处理（后续探测给出准确错误语义）。
        /// </summary>
        /// <param name="saveFilePath">存档文件完整路径。</param>
        /// <returns>缓存有效命中返回 <c>true</c>。</returns>
        private static bool IsSessionMigrated(string saveFilePath)
        {
            if (!s_MigratedPaths.TryGetValue(saveFilePath, out DateTime markedTimeUtc))
            {
                return false;
            }

            try
            {
                return markedTimeUtc == File.GetLastWriteTimeUtc(saveFilePath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 标记会话迁移缓存（须在 <see cref="s_SessionLock"/> 内调用；记录当前文件写入时间作为有效性锚点）。
        /// </summary>
        /// <param name="saveFilePath">存档文件完整路径。</param>
        private static void MarkSessionMigrated(string saveFilePath)
        {
            DateTime markedTimeUtc;
            try
            {
                markedTimeUtc = File.GetLastWriteTimeUtc(saveFilePath);
            }
            catch (Exception)
            {
                markedTimeUtc = DateTime.MinValue; // 读取失败：与命中判定同为失败语义（不会误命中）
            }

            s_MigratedPaths[saveFilePath] = markedTimeUtc;
        }

        #endregion

        #region 迁移执行 [MIGRATION]

        /// <summary>
        /// 按需迁移存档块集合到当前数据版本（纯数据操作，不落盘——回写由处理器按设置执行）。
        /// <para>版本相等/未激活/空档/同会话已迁移短路；缺档语义由调用方保证（空块集不进入本方法）。</para>
        /// </summary>
        /// <param name="paths">已解析的路径集合（日志与审计上下文）。</param>
        /// <param name="blocks">健康数据块列表（含元数据块）。</param>
        /// <param name="blockErrors">坏块清单（元数据块损坏时保守失败——版本不可信）。</param>
        /// <param name="migratedBlocks">迁移后的块集合（未迁移时为源列表）。</param>
        /// <param name="migrated">是否实际执行了迁移。</param>
        /// <returns>错误码：<see cref="SaveError.None"/>、<see cref="SaveError.UnsupportedVersion"/>（降级拒绝）或 <see cref="SaveError.MigrationFailed"/>。</returns>
        internal static SaveError TryMigrateBlocks(SaveServiceHandler.SavePaths paths, List<SaveBlockEntry> blocks, List<SaveBlockError> blockErrors, out List<SaveBlockEntry> migratedBlocks, out bool migrated)
        {
            migratedBlocks = blocks;
            migrated = false;
            if (!IsActive || blocks.Count == 0)
            {
                return SaveError.None;
            }

            lock (s_SessionLock)
            {
                if (IsSessionMigrated(paths.SaveFilePath))
                {
                    return SaveError.None;
                }
            }

            SaveError metaError = TryReadSaveVersion(blocks, blockErrors, out int fileVersion, out SaveMetadata metadata);
            if (metaError != SaveError.None)
            {
                LogUtility.Error("[SaveService] Save version probe failed, path: {0}, error: {1}.", paths.SaveFilePath, metaError);
                return metaError;
            }

            int currentVersion = s_CurrentVersion;
            if (fileVersion > currentVersion)
            {
                // 降级拒绝：存档由更新版本运行时写出
                LogUtility.Error("[SaveService] Save downgrade rejected, path: {0}, file version: {1}, current version: {2}.", paths.SaveFilePath, fileVersion, currentVersion);
                return SaveError.UnsupportedVersion;
            }

            if (fileVersion == currentVersion)
            {
                // 版本相等同样进会话缓存——同文件后续加载免重复元数据探测
                lock (s_SessionLock)
                {
                    MarkSessionMigrated(paths.SaveFilePath);
                }

                return SaveError.None;
            }

            ISaveMigrator[] migrators = SnapshotMigrators();
            List<string> history = metadata?.MigrationHistory;
            List<SaveBlockEntry> workingBlocks = blocks;
            int stepVersion = fileVersion;
            int steps = 0;
            while (stepVersion < currentVersion)
            {
                if (++steps > MaxChainSteps)
                {
                    LogUtility.Error("[SaveService] Save migration chain exceeds the step cap ({0}), path: {1}.", MaxChainSteps, paths.SaveFilePath);
                    return SaveError.MigrationFailed;
                }

                SaveError stepError = ResolveChainStep(migrators, stepVersion, out int nextVersion);
                if (stepError != SaveError.None)
                {
                    LogUtility.Error("[SaveService] Save migration chain broken, path: {0}, from version: {1}, error: {2}.", paths.SaveFilePath, stepVersion, stepError);
                    return stepError;
                }

                // 同一边（From == 步版本 && To == 下一步版本）的迁移器按 Priority 升序执行
                int edgeCount = CollectEdgeMigrators(migrators, stepVersion, nextVersion, out ISaveMigrator[] edgeMigrators);
                for (int i = 0; i < edgeCount; i++)
                {
                    ISaveMigrator migrator = edgeMigrators[i];
                    var context = new SaveMigrationContext(workingBlocks, stepVersion, nextVersion, paths.FileName, paths.FolderName);
                    SaveError runError = RunMigrator(migrator, context);
                    if (runError != SaveError.None)
                    {
                        LogUtility.Error("[SaveService] Save migrator '{0}' failed, path: {1}, detail: {2}.", migrator.GetType().FullName, paths.SaveFilePath, context.ErrorDetail);
                        return runError;
                    }

                    workingBlocks = context.Blocks;
                    AppendHistory(ref history, stepVersion, nextVersion, migrator);
                }

                stepVersion = nextVersion;
            }

            // 元数据块 upsert：版本推进 + 审计历史（回写/后续合并写由处理器决定）
            metadata ??= new SaveMetadata();
            metadata.SaveVersion = currentVersion;
            metadata.MigrationHistory = history;
            migratedBlocks = UpsertMetaBlock(workingBlocks, metadata);
            migrated = true;

            lock (s_SessionLock)
            {
                MarkSessionMigrated(paths.SaveFilePath);
            }

            LogUtility.Info("[SaveService] Save migrated, path: {0}, version: {1} -> {2}, steps: {3}.", paths.SaveFilePath, fileVersion, currentVersion, steps);
            return SaveError.None;
        }

        /// <summary>
        /// 版本探测：元数据块缺失按版本 0；元数据块损坏（逐块 CRC 坏块命中）保守失败；反序列化失败归一迁移失败。
        /// </summary>
        /// <param name="blocks">健康数据块列表。</param>
        /// <param name="blockErrors">坏块清单。</param>
        /// <param name="fileVersion">探测到的存档数据版本。</param>
        /// <param name="metadata">元数据对象（无元数据块为 null）。</param>
        /// <returns>错误码。</returns>
        private static SaveError TryReadSaveVersion(List<SaveBlockEntry> blocks, List<SaveBlockError> blockErrors, out int fileVersion, out SaveMetadata metadata)
        {
            fileVersion = 0;
            metadata = null;

            if (SaveBlockComposer.TryFind(blocks, SaveServiceHandler.META_BLOCK_KEY, out SaveBlockEntry metaEntry))
            {
                try
                {
                    metadata = SaveSerializerRegistry.GetRequired(ESaveBackend.Json).Deserialize<SaveMetadata>(metaEntry.Bytes);
                }
                catch (Exception)
                {
                    return SaveError.MigrationFailed;
                }

                fileVersion = metadata?.SaveVersion ?? 0;
                return SaveError.None;
            }

            // 元数据块损坏（框架完好的 CRC 坏块）——版本不可信，保守失败
            if (blockErrors != null)
            {
                for (int i = 0; i < blockErrors.Count; i++)
                {
                    if (string.Equals(blockErrors[i].Key, SaveServiceHandler.META_BLOCK_KEY, StringComparison.Ordinal))
                    {
                        return SaveError.Corrupted;
                    }
                }
            }

            return SaveError.None;
        }

        /// <summary>
        /// 解析迁移链单步：同起始版本的迁移器目标版本必须唯一（歧义/缺失 fail-fast）。
        /// </summary>
        /// <param name="migrators">迁移器快照。</param>
        /// <param name="stepVersion">当前步版本。</param>
        /// <param name="nextVersion">解析出的下一步版本。</param>
        /// <returns>错误码。</returns>
        private static SaveError ResolveChainStep(ISaveMigrator[] migrators, int stepVersion, out int nextVersion)
        {
            nextVersion = -1;
            for (int i = 0; i < migrators.Length; i++)
            {
                if (migrators[i].FromVersion != stepVersion)
                {
                    continue;
                }

                if (nextVersion < 0)
                {
                    nextVersion = migrators[i].ToVersion;
                }
                else if (nextVersion != migrators[i].ToVersion)
                {
                    // 同一起始版本存在多条不同目标版本的边——链歧义，fail-fast
                    return SaveError.MigrationFailed;
                }
            }

            return nextVersion < 0 ? SaveError.MigrationFailed : SaveError.None;
        }

        /// <summary>
        /// 收集同一边的迁移器并按 Priority 升序排序（插入排序，边内数量极小）。
        /// </summary>
        /// <param name="migrators">迁移器快照。</param>
        /// <param name="stepVersion">当前步版本。</param>
        /// <param name="nextVersion">下一步版本。</param>
        /// <param name="edgeMigrators">排序后的边迁移器数组（长度可能大于返回计数）。</param>
        /// <returns>边迁移器计数。</returns>
        private static int CollectEdgeMigrators(ISaveMigrator[] migrators, int stepVersion, int nextVersion, out ISaveMigrator[] edgeMigrators)
        {
            var collected = new List<ISaveMigrator>(4);
            for (int i = 0; i < migrators.Length; i++)
            {
                if (migrators[i].FromVersion == stepVersion && migrators[i].ToVersion == nextVersion)
                {
                    collected.Add(migrators[i]);
                }
            }

            for (int i = 1; i < collected.Count; i++)
            {
                ISaveMigrator current = collected[i];
                int j = i - 1;
                while (j >= 0 && collected[j].Priority > current.Priority)
                {
                    collected[j + 1] = collected[j];
                    j--;
                }

                collected[j + 1] = current;
            }

            edgeMigrators = collected.ToArray();
            return collected.Count;
        }

        /// <summary>
        /// 执行单个迁移器（同步完成断言：返回未完成任务或抛异常均归一迁移失败）。
        /// </summary>
        /// <param name="migrator">迁移器。</param>
        /// <param name="context">迁移上下文（起始/目标版本为当前步版本）。</param>
        /// <returns>错误码。</returns>
        private static SaveError RunMigrator(ISaveMigrator migrator, SaveMigrationContext context)
        {
            try
            {
                UniTask task = migrator.Migrate(context);
                if (!task.GetAwaiter().IsCompleted)
                {
                    context.RecordError(StringUtility.Format("Migrator '{0}' returned an incomplete task (migrations must complete synchronously).", migrator.GetType().FullName));
                    return SaveError.MigrationFailed;
                }

                task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                context.RecordError(StringUtility.Format("Migrator '{0}' threw {1}: {2}.", migrator.GetType().FullName, exception.GetType().Name, exception.Message));
                return SaveError.MigrationFailed;
            }

            return context.ErrorDetail == null ? SaveError.None : SaveError.MigrationFailed;
        }

        /// <summary>
        /// 追加迁移历史审计记录。
        /// </summary>
        /// <param name="history">历史列表（null 时创建）。</param>
        /// <param name="fromVersion">迁移步起始版本。</param>
        /// <param name="toVersion">迁移步目标版本。</param>
        /// <param name="migrator">迁移器。</param>
        private static void AppendHistory(ref List<string> history, int fromVersion, int toVersion, ISaveMigrator migrator)
        {
            history ??= new List<string>();
            history.Add(StringUtility.Format("{0}->{1}|{2}|{3:O}", fromVersion, toVersion, migrator.GetType().FullName, DateTime.UtcNow));
        }

        /// <summary>
        /// 将更新后的元数据 upsert 进迁移后的块集合（保留既有元数据字段，仅推进版本与追加历史；
        /// 迁移器删除/改名过元数据块的极端场景以迁移后块集合为准重新 upsert）。
        /// </summary>
        /// <param name="migratedBlocks">迁移器改写后的块集合。</param>
        /// <param name="metadata">更新后的元数据对象。</param>
        /// <returns>含最新元数据块的块集合。</returns>
        private static List<SaveBlockEntry> UpsertMetaBlock(List<SaveBlockEntry> migratedBlocks, SaveMetadata metadata)
        {
            byte[] metaBytes = SaveSerializerRegistry.GetRequired(ESaveBackend.Json).Serialize(metadata);
            var newMetaEntry = new SaveBlockEntry(SaveServiceHandler.META_BLOCK_KEY, 1, ESaveBackend.Json, metaBytes);
            return SaveBlockComposer.Upsert(migratedBlocks, newMetaEntry);
        }

        #endregion
    }
}
