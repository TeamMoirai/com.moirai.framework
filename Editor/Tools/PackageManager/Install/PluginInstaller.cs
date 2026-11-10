using System;
using System.Collections.Generic;
using System.IO;
using Moirai.Atropos;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 装卸编排：把包源层、发现层、规划层与账本按固定时序串起来。
    /// </summary>
    /// <remarks>
    /// 检出落在 <c>Library/MoiraiCache/</c>，只是刷新与取内容的中间层；安装把插件从检出复制到 <c>Packages/&lt;检出目录名&gt;.&lt;id&gt;/</c>，
    /// 并补一份最小的 <c>package.json</c> 让 Unity 把它当嵌入包加载。<br />
    /// 时序：建检出 → 摆正分支 → fetch → 再摆正（fetch 后本地分支才看得见新提交）→ 列树 → 建目录 → 规划，
    /// 规划判出冲突或目标目录归属不明即整批中止，此刻还没写过任何东西。<br />
    /// 落盘顺序收在"先复制安装目录，再写工程 manifest 与脚本宏，再把状态账本落盘，最后只刷新一次"：刷新会触发域重载，
    /// 账本晚一步就没机会写完。<br />
    /// 卸载的依据全部来自账本：分支上那份清单消失了也要能回收，不靠扫描工作树反推。
    /// </remarks>
    internal sealed class PluginInstaller
    {
        private const string ROOT_PACKAGE_JSON_NAME = "package.json";
        private const int ENGINE_VERSION_SEGMENT_COUNT = 3;

        // 检出只是临时工作树，安装目标是 Packages 下的独立嵌入包。
        // Unity 只把带 package.json 的 Packages 子目录当包，且技术名须与目录名一致，
        // 因此目标目录用「检出目录名.id」，包名也写同一个值。
        private const string INSTALL_FOLDER_SEPARATOR = ".";

        private readonly string _sourceRootPath;
        private readonly string _installRootPath;
        private readonly string _projectManifestPath;
        private readonly PluginPackageSource _source;
        private readonly PackageManagerStateRegistry _stateRegistry;
        private readonly PackageManagerSettingsData _settings;
        private readonly Type _hostPackageType;
        private bool _lastFetchReachedRemote;

        internal PluginInstaller(PackageManagerSettingsData settings, string sourceRootPath, string installRootPath,
            string projectManifestPath, PackageManagerStateRegistry stateRegistry, Type hostPackageType)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (stateRegistry == null) throw new ArgumentNullException(nameof(stateRegistry));
            if (string.IsNullOrEmpty(sourceRootPath)) throw new ArgumentException("检出目录不能为空", nameof(sourceRootPath));
            if (string.IsNullOrEmpty(installRootPath)) throw new ArgumentException("安装目录不能为空", nameof(installRootPath));

            _settings = settings;
            _sourceRootPath = Path.GetFullPath(sourceRootPath);
            _installRootPath = Path.GetFullPath(installRootPath);
            _projectManifestPath = Path.GetFullPath(projectManifestPath);
            _stateRegistry = stateRegistry;
            _hostPackageType = hostPackageType;
            _source = new PluginPackageSource(_sourceRootPath, settings.repoUrl, settings.branch, settings.gitPath);
        }

        /// <summary>
        /// 按一套包源配置建一个编排器：检出落 <c>Library/MoiraiCache/</c>，安装与工程 manifest 在 <c>Packages/</c>。
        /// </summary>
        internal static PluginInstaller Create(PackageManagerSettingsData settings,
            PackageManagerWindowBase.PackageManagerProfile profile, PackageManagerStateRegistry stateRegistry)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string packagesRoot = Path.Combine(Directory.GetCurrentDirectory(), "Packages");
            return new PluginInstaller(settings,
                PackageManagerStateRegistry.ResolveDefaultWorkspacePath(profile.WorkspaceFolderName),
                packagesRoot,
                Path.Combine(packagesRoot, "manifest.json"), stateRegistry, profile.HostPackageType);
        }

        /// <summary>包源层，窗口取源状态用。</summary>
        internal PluginPackageSource Source => _source;

        /// <summary>本轮 fetch 是否真到了远端；没到就不该记"上次刷新"时刻。</summary>
        internal bool LastFetchReachedRemote => _lastFetchReachedRemote;

        /// <summary>
        /// 安装各阶段的进度回调（0..1 + 当前阶段文案）；窗口用它边装边画进度，用例留空即不报。
        /// </summary>
        internal Action<float, string> Progress { get; set; }

        /// <summary>同名目录已存在又不在账本上时，把覆盖不覆盖交给谁拍板：返回 true 即先删掉这些目录再物化。</summary>
        /// <remarks>编排层不自建对话框，也不替用户默认批出去——没挂口子一律判拒，用例各挂一条恒真与恒假回调即可判定两翼。</remarks>
        internal Func<IReadOnlyList<string>, bool> ConfirmOverwriteForeignFolders { get; set; }

        private void ReportProgress(float value, string stage)
        {
            Progress?.Invoke(value, stage);
        }

        /// <summary>
        /// 取当前编辑器版本，形状不符时回最低地板并交出原因。
        /// </summary>
        internal static bool TryGetEngineVersion(out PluginVersion version, out string failureReason)
        {
            if (TryParseEngineVersion(Application.unityVersion, out version))
            {
                failureReason = null;
                return true;
            }

            failureReason = $"读不懂编辑器版本号 '{Application.unityVersion}'";
            version = new PluginVersion(2022, 3, 0);
            return false;
        }

        /// <summary>
        /// 取宿主包版本（清单里 <c>minFrameworkVersion</c> / <c>maxFrameworkVersion</c> 的对照方）；
        /// 没给宿主类型或读不到时交出 <c>0.0.0</c> 与原因（那一刻不判这段区间，宁可放过也不误拦）。
        /// </summary>
        internal static bool TryGetHostPackageVersion(Type hostPackageType, out PluginVersion version, out string failureReason)
        {
            if (hostPackageType == null)
            {
                version = default;
                failureReason = "未指定宿主包类型，本轮不判 minFrameworkVersion / maxFrameworkVersion";
                return false;
            }

            PackageInfo info = PackageInfo.FindForAssembly(hostPackageType.Assembly);
            if (info != null && PluginVersion.TryParse(info.version, out version))
            {
                failureReason = null;
                return true;
            }

            version = default;
            failureReason = info == null
                ? "读不出宿主包信息，本轮不判 minFrameworkVersion / maxFrameworkVersion"
                : $"宿主包版本号 '{info.version}' 不是三段十进制，本轮不判 minFrameworkVersion / maxFrameworkVersion";
            return false;
        }

        /// <summary>从引擎版本号里取前三段十进制。</summary>
        /// <remarks>修订段常带发行后缀（<c>2022.3.62f3</c>、<c>6000.3.26f1</c>），只取它的前导数字。</remarks>
        internal static bool TryParseEngineVersion(string engineVersion, out PluginVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(engineVersion)) return false;

            string[] parts = engineVersion.Split('.');
            if (parts.Length < ENGINE_VERSION_SEGMENT_COUNT) return false;
            if (!ContainsOnlyDigits(parts[0]) || !ContainsOnlyDigits(parts[1])) return false;

            string patch = LeadingDigits(parts[2]);
            if (patch.Length == 0) return false;

            return PluginVersion.TryParse(parts[0] + "." + parts[1] + "." + patch, out version);
        }

        private static string LeadingDigits(string text)
        {
            int end = 0;
            while (end < text.Length && text[end] >= '0' && text[end] <= '9') end++;
            return text.Substring(0, end);
        }

        /// <summary>
        /// 走一遍源侧读取，产出目录与分支头。
        /// </summary>
        internal bool TryBrowse(PluginInstallReport report, out PluginCatalogResult catalog, out string headCommit)
        {
            catalog = null;
            headCommit = null;
            _lastFetchReachedRemote = false;

            // 刚建出来的检出已经站在分支头上，再 fetch 一遍就是白跑一趟网络
            bool clonedNow = !_source.IsGitWorkspace;
            if (!_source.EnsureClone(out string reason))
            {
                report.Fail(reason);
                return false;
            }

            report.Note("落点工作树就绪");

            // 离线不算失败：本地已有的分支头与缓存 blob 足够列出与装（blob 真取不到时由清单读取那步判非并说明）
            if (clonedNow)
            {
                _lastFetchReachedRemote = true;
                report.Note("新建检出，已含分支头");
            }
            else if (!_source.Fetch(out reason))
            {
                report.Note($"离线：fetch 没走通（{reason}），本轮按本地已有的分支头继续");
            }
            else
            {
                _lastFetchReachedRemote = true;
                report.Note($"已取 {(_settings.branch ?? string.Empty)} 分支头");
            }

            // 先列树、把根文件与清单一次预取，再摆正分支：摆正只物化勾选目录，正文已在本地就不会逐份联网
            string remoteReference = "refs/remotes/origin/" + _source.Branch;
            if (!_source.TryListBlobs(remoteReference, out List<PluginPackageSource.TreeBlob> blobs, out reason))
            {
                report.Fail(reason);
                return false;
            }

            var treePaths = new List<string>(blobs.Count);
            var oidByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < blobs.Count; i++)
            {
                treePaths.Add(blobs[i].Path);
                oidByPath[blobs[i].Path] = blobs[i].ObjectId;
            }

            List<string> manifestPaths = PluginCatalog.SelectManifestPaths(treePaths);
            List<string> neededObjectIds = CollectBrowseObjectIds(blobs, manifestPaths, oidByPath);
            if (!_source.TryEnsureBlobsPresent(remoteReference, neededObjectIds, out string prefetchReason))
            {
                // 预取只决定快慢：读文那步仍会按需取，真取不到由清单读取报出来
                report.Note($"清单预取没走通，逐份重读：{prefetchReason}");
            }

            if (!_source.EnsureBranchAttached(out reason))
            {
                report.Fail(reason);
                return false;
            }

            var textsByPath = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!_source.TryReadBlobs(neededObjectIds, out Dictionary<string, string> textsByObjectId, out string readReason))
            {
                report.Note($"批量取文没走通，逐份重读：{readReason}");
            }
            else
            {
                for (int i = 0; i < blobs.Count; i++)
                {
                    if (textsByObjectId.TryGetValue(blobs[i].ObjectId, out string text)) textsByPath[blobs[i].Path] = text;
                }
            }

            catalog = PluginCatalog.Build(treePaths, path =>
            {
                if (textsByPath.TryGetValue(path, out string text)) return text;
                return _source.TryReadBlob(path, out string fallback, out _) ? fallback : null;
            });

            for (int i = 0; i < catalog.Errors.Count; i++) report.Note(catalog.Errors[i]);

            if (!ValidateRootPackageJson(catalog, textsByPath, report, out reason))
            {
                report.Fail(reason);
                return false;
            }

            if (!_source.TryHeadCommit(out headCommit, out reason))
            {
                report.Fail(reason);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 浏览一轮要预取的对象：根文件（供包名校验，不物化到工作树）与各份清单。
        /// </summary>
        private static List<string> CollectBrowseObjectIds(List<PluginPackageSource.TreeBlob> blobs,
            List<string> manifestPaths, Dictionary<string, string> oidByPath)
        {
            var needed = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < blobs.Count; i++)
            {
                if (blobs[i].Path.IndexOf('/') >= 0) continue;
                if (seen.Add(blobs[i].ObjectId)) needed.Add(blobs[i].ObjectId);
            }

            for (int i = 0; i < manifestPaths.Count; i++)
            {
                if (!oidByPath.TryGetValue(manifestPaths[i], out string oid)) continue;
                if (seen.Add(oid)) needed.Add(oid);
            }

            return needed;
        }

        /// <summary>
        /// 装一个插件，连它的依赖闭包一起。
        /// </summary>
        internal PluginInstallReport Install(string pluginId, bool force)
        {
            return Install(new[] { pluginId }, force);
        }

        /// <summary>
        /// 装一个插件；<paramref name="removeFirst"/> 为真时先清掉它的目录（干净更新）。
        /// </summary>
        internal PluginInstallReport Install(string pluginId, bool force, bool removeFirst)
        {
            return Install(new[] { pluginId }, force, removeFirst ? new[] { pluginId } : null);
        }

        /// <summary>装某个插件的历史修订：先清目录再按该修订物化，账本记该修订与它那次的版本。</summary>
        /// <remarks>清目录是必须的：不先删，<c>sparse-checkout</c> 的合并语义会把分支头那份残留文件留下来。</remarks>
        internal PluginInstallReport InstallAtRevision(string pluginId, string revision, string version)
        {
            return Install(new[] { pluginId }, true, new[] { pluginId }, revision, version);
        }

        /// <summary>
        /// 装一批插件：一次勾选集写入、一次 manifest 写入、一次刷新。
        /// </summary>
        /// <remarks>分批装会在每个包后各触发一次域重载，第二趟起就没人在跑了——多包必须走这里。<br />
        /// <paramref name="revision"/> 非空即"装历史修订"：只装点名的这批、账本记该修订（批次内共用同一个修订）。</remarks>
        internal PluginInstallReport Install(IReadOnlyList<string> pluginIds, bool force,
            IReadOnlyList<string> removeFirstIds = null, string revision = null, string revisionVersion = null)
        {
            var report = new PluginInstallReport();
            ReportProgress(0.02f, "检查检出");

            if (!TryGetEngineVersion(out PluginVersion unityVersion, out string versionReason)) report.Note(versionReason);
            if (!TryGetHostPackageVersion(_hostPackageType, out PluginVersion hostVersion, out string frameworkReason)) report.Note(frameworkReason);
            ReportProgress(0.08f, "扫分支与取目录");
            if (!TryBrowse(report, out PluginCatalogResult catalog, out string headCommit)) return report;

            ReportProgress(0.4f, "规划依赖闭包");
            PackageManagerStateData state = _stateRegistry.Load();
            List<string> installedIds = InstalledIds(state);

            InstallPlan plan = InstallPlanner.Plan(catalog.Manifests, installedIds, pluginIds,
                force ? pluginIds : NoIds, unityVersion, hostVersion);

            for (int i = 0; i < plan.Conflicts.Count; i++) report.AddConflict(plan.Conflicts[i]);

            if (plan.HasConflicts)
            {
                report.Fail("规划判出冲突，整批中止：" + string.Join("；", ConflictReasons(plan)));
                return report;
            }

            if (plan.ToInstall.Count == 0)
            {
                report.Note("点名的插件都已装且满足，本次没有改动");
                return report;
            }

            if (!ValidateInstallTargets(plan, installedIds, out IReadOnlyList<string> overwriteIds, out string targetReason))
            {
                report.Fail(targetReason);
                return report;
            }

            List<string> selectedIds = Union(installedIds, plan);

            // 干净更新：先清目录（含刚批准要顶掉的外来同名目录），随后物化时按分支头重新落一遍，清掉新版本不再产出的文件
            ReportProgress(0.45f, "清理与物化插件目录");
            RemoveFolders(MergeIds(removeFirstIds, overwriteIds), report);
            if (!report.Ok) return report;

            if (!_source.ApplySparsePaths(selectedIds, out string sparseReason))
            {
                report.Fail(sparseReason + "　若已清过目录，重跑一次同样的安装即可对齐（账本没动）");
                return report;
            }

            report.Note($"勾选集收到 {selectedIds.Count} 项");

            // 装历史版本：目录已清过，这里按该修订把它们物化回来（排在 .asmdef 校验之前——校验要读盘上的文件）
            if (!string.IsNullOrEmpty(revision) && !_source.TryMaterializeAt(revision, pluginIds, out string materializeReason))
            {
                report.Fail(materializeReason);
                return report;
            }

            if (!ValidateAssemblyDefinitions(plan, selectedIds, report, out string asmdefReason))
            {
                report.Fail(asmdefReason);
                return report;
            }

            ReportProgress(0.6f, "复制到 Packages");
            if (!CopyInstalledFolders(plan, out string copyReason))
            {
                report.Fail(copyReason);
                return report;
            }

            report.Note($"已落盘 {plan.ToInstall.Count} 个包到 Packages/");

            ReportProgress(0.8f, "写工程 manifest 与脚本宏");
            if (!WriteProjectManifest(state, plan, report, out string manifestReason))
            {
                report.Fail(manifestReason);
                return report;
            }

            foreach (PluginManifest manifest in plan.ToInstall)
            {
                string[] tags = manifest.tags;
                for (int i = 0; tags != null && i < tags.Length; i++) ScriptingDefineSymbols.AddScriptingDefineSymbol(tags[i]);

                // 装历史版本时账本要记该修订与它那次的版本，不能用分支头这份
                bool atRevision = !string.IsNullOrEmpty(revision);
                string recordedVersion = atRevision ? revisionVersion ?? manifest.version : manifest.version;

                _stateRegistry.UpsertInstalled(state, new InstalledPluginData
                {
                    id = manifest.id,
                    version = recordedVersion,
                    commit = atRevision ? revision : headCommit,
                    function = manifest.function,
                    tags = tags ?? Array.Empty<string>()
                });

                report.AddTouched(manifest.id);
                report.Note($"装了 {manifest.id} {recordedVersion}");
            }

            _stateRegistry.Save(state);
            report.Note("状态账本已落盘");

            ReportProgress(0.95f, "刷新资源");
            AssetDatabase.Refresh();
            ReportProgress(1f, "完成");
            return report;
        }

        /// <summary>
        /// 卸一个插件，按账本回收外部依赖与脚本宏。
        /// </summary>
        internal PluginInstallReport Uninstall(string pluginId, bool cascadeDependents)
        {
            var report = new PluginInstallReport();
            PackageManagerStateData state = _stateRegistry.Load();

            InstalledPluginData entry = _stateRegistry.FindInstalled(state, pluginId);
            if (entry == null)
            {
                report.Fail($"账本里没有 '{pluginId}'，不凭目录猜装了什么");
                return report;
            }

            if (!TryBrowse(report, out PluginCatalogResult catalog, out _)) return report;

            IReadOnlyList<string> dependents = InstallPlanner.DependentsOf(catalog.Manifests, pluginId, InstalledIds(state));
            for (int i = 0; i < dependents.Count; i++) report.AddDependent(dependents[i]);

            var removalIds = new List<string> { pluginId };
            if (cascadeDependents)
            {
                for (int i = 0; i < dependents.Count; i++)
                {
                    if (!removalIds.Contains(dependents[i])) removalIds.Add(dependents[i]);
                }
            }

            if (dependents.Count > 0)
            {
                report.Note(cascadeDependents
                    ? $"连带卸载在场依赖方：{string.Join("、", dependents)}"
                    : $"只卸 {pluginId}，这些依赖方留在场且会缺它：{string.Join("、", dependents)}");
            }

            // 先算后写：稀疏集、工程 manifest、宏、账本全部判通了才动第一次盘
            List<string> remainingIds = InstalledIds(state);
            for (int i = 0; i < removalIds.Count; i++) remainingIds.Remove(removalIds[i]);

            string baseline = ReadProjectManifest(out string readReason);
            if (baseline == null)
            {
                report.Fail(readReason);
                return report;
            }

            string manifestText = baseline;
            bool textChanged = false;
            var ownedPackageIds = new List<string>();
            for (int i = 0; i < state.upmOwnership.Count; i++) ownedPackageIds.Add(state.upmOwnership[i].packageId);

            for (int i = 0; i < removalIds.Count; i++)
            {
                for (int j = 0; j < ownedPackageIds.Count; j++)
                {
                    UpmEntryOutcome outcome = _stateRegistry.ReleaseUpmOwner(state, manifestText,
                        ownedPackageIds[j], removalIds[i], out string released);
                    if (!ReferenceEquals(released, manifestText))
                    {
                        manifestText = released;
                        textChanged = true;
                    }

                    if (outcome != UpmEntryOutcome.NotFound)
                        report.Note($"外部依赖 {ownedPackageIds[j]}（{removalIds[i]}）：{outcome}");
                }
            }

            if (!_source.ApplySparsePaths(remainingIds, out string sparseReason))
            {
                report.Fail(sparseReason + "　磁盘与账本都没改");
                return report;
            }

            report.Note($"勾选集缩到 {remainingIds.Count} 项");

            RemoveFolders(removalIds, report);
            if (!report.Ok) return report;

            if (textChanged && !TryWriteProjectManifest(baseline, manifestText, out string writeReason))
            {
                report.Fail(writeReason + "　勾选集已改，账本未改，重来一次即可对齐");
                return report;
            }

            HashSet<string> stillClaimed = TagsClaimedBy(state, removalIds);
            for (int i = 0; i < removalIds.Count; i++)
            {
                InstalledPluginData removal = _stateRegistry.FindInstalled(state, removalIds[i]);
                if (removal == null) continue;

                RemoveOrphanTags(removal, stillClaimed, report);
                _stateRegistry.RemoveInstalled(state, removal.id);
                report.AddTouched(removal.id);
            }

            _stateRegistry.Save(state);
            report.Note("状态账本已落盘");

            AssetDatabase.Refresh();
            return report;
        }

        /// <summary>
        /// 取仍被留下那批插件声明的宏集合，用来判哪些宏可以撤。
        /// </summary>
        private static HashSet<string> TagsClaimedBy(PackageManagerStateData state, List<string> removalIds)
        {
            var kept = new HashSet<string>(StringComparer.Ordinal);
            if (state?.installed == null) return kept;

            for (int i = 0; i < state.installed.Count; i++)
            {
                InstalledPluginData other = state.installed[i];
                if (other == null || removalIds.Contains(other.id)) continue;

                string[] otherTags = other.tags;
                for (int j = 0; otherTags != null && j < otherTags.Length; j++)
                {
                    if (!string.IsNullOrEmpty(otherTags[j])) kept.Add(otherTags[j]);
                }
            }

            return kept;
        }

        /// <summary>
        /// 判定某个已装插件在新分支头上有没有内容变化。
        /// </summary>
        internal bool TryIsUpdateAvailable(InstalledPluginData entry, out bool available, out string failureReason)
        {
            available = false;
            if (entry == null || string.IsNullOrEmpty(entry.id))
            {
                failureReason = "缺 id 的记账项不能判更新";
                return false;
            }

            if (!TryTreeHashOf(entry.id, entry.commit, out string installedHash, out failureReason)) return false;
            if (!TryTreeHashOf(entry.id, "refs/remotes/origin/" + _source.Branch, out string branchHash, out failureReason)) return false;

            available = !string.Equals(installedHash, branchHash, StringComparison.Ordinal);
            return true;
        }

        /// <summary>
        /// 取某修订下该插件目录的 tree 哈希。
        /// </summary>
        /// <remarks>取整个目录而非只取清单：分支上删掉一个源文件时清单往往没变，只比清单会漏判"可更新"。</remarks>
        internal bool TryTreeHashOf(string pluginId, string revision, out string hash, out string failureReason)
        {
            return _source.TryTreeHash(pluginId, revision, out hash, out failureReason);
        }

        /// <summary>
        /// 逐条判在场插件有没有内容变化；任一次读不通即交出原因。
        /// </summary>
        internal bool TryCollectUpdatable(PackageManagerStateData state, out List<string> updatableIds,
            out string failureReason)
        {
            updatableIds = new List<string>();
            List<string> installedIds = InstalledIds(state);

            for (int i = 0; i < installedIds.Count; i++)
            {
                InstalledPluginData entry = _stateRegistry.FindInstalled(state, installedIds[i]);
                if (!TryIsUpdateAvailable(entry, out bool available, out failureReason)) return false;
                if (available) updatableIds.Add(entry.id);
            }

            failureReason = null;
            return true;
        }

        private static bool ContainsOnlyDigits(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9') return false;
            }

            return true;
        }

        private static List<string> InstalledIds(PackageManagerStateData state)
        {
            var ids = new List<string>();
            if (state?.installed == null) return ids;

            for (int i = 0; i < state.installed.Count; i++)
            {
                if (!string.IsNullOrEmpty(state.installed[i].id)) ids.Add(state.installed[i].id);
            }

            return ids;
        }

        private static List<string> Union(List<string> installedIds, InstallPlan plan)
        {
            var union = new List<string>(installedIds);
            foreach (PluginManifest manifest in plan.ToInstall)
            {
                if (!union.Contains(manifest.id)) union.Add(manifest.id);
            }

            return union;
        }

        private static List<string> ConflictReasons(InstallPlan plan)
        {
            var reasons = new List<string>();
            foreach (PlanConflict conflict in plan.Conflicts) reasons.Add($"{conflict.PluginId}：{conflict.Reason}");
            return reasons;
        }

        private static string[] NoIds => Array.Empty<string>();

        /// <summary>删掉这些插件的安装目录（干净更新或被卸载的包）。</summary>
        /// <remarks>删除面与 git 面同款纪律：目录名先过 <see cref="PluginManifest.IsValidId"/>，拼不出去才动手。</remarks>
        private void RemoveFolders(IReadOnlyList<string> ids, PluginInstallReport report)
        {
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                if (!PluginManifest.IsValidId(ids[i])) continue;

                string folder = InstallFolderPath(ids[i]);
                if (!Directory.Exists(folder)) continue;

                try
                {
                    Directory.Delete(folder, true);
                }
                catch (Exception exception)
                {
                    report.Fail($"删不掉 {ids[i]} 的安装目录：{exception.Message}");
                    return;
                }

                report.Note($"已清掉 {ids[i]} 的安装目录");
            }
        }

        /// <summary>某个插件在工程里的安装目录：<c>Packages/&lt;检出目录名&gt;.&lt;id&gt;/</c>。</summary>
        internal string InstallFolderPath(string pluginId)
        {
            return Path.Combine(_installRootPath, new DirectoryInfo(_sourceRootPath).Name + INSTALL_FOLDER_SEPARATOR + pluginId);
        }

        /// <summary>
        /// 安装目标目录先过一遍：已存在又不在账本上的同名目录逐个交出去批，批下来的进先删后装的名单。
        /// </summary>
        /// <remarks>检出住在 Library，目标住在 Packages；后者可能有用户手放的同名包，工具不擅自顶掉，也不擅自拒绝——交 <see cref="ConfirmOverwriteForeignFolders"/> 决策。<br />
        /// 批不下来的整批中止，此刻还没碰过盘；批下来的连同 <paramref name="overwriteIds"/> 一起交给调用段去删。<br />
        /// 目标位上是个文件时不给过：清目录只删目录，删完照样建不出包。</remarks>
        private bool ValidateInstallTargets(InstallPlan plan, List<string> installedIds,
            out IReadOnlyList<string> overwriteIds, out string failureReason)
        {
            overwriteIds = null;
            var foreignIds = new List<string>();
            var foreignFolders = new List<string>();

            for (int i = 0; i < plan.ToInstall.Count; i++)
            {
                string id = plan.ToInstall[i].id;
                string folder = InstallFolderPath(id);

                if (File.Exists(folder))
                {
                    failureReason = $"{folder} 是个文件，不能作安装目录";
                    return false;
                }

                if (!Directory.Exists(folder) || installedIds.Contains(id)) continue;

                foreignIds.Add(id);
                foreignFolders.Add(folder);
            }

            if (foreignFolders.Count == 0)
            {
                failureReason = null;
                return true;
            }

            if (ConfirmOverwriteForeignFolders == null || !ConfirmOverwriteForeignFolders(foreignFolders))
            {
                failureReason = $"这 {foreignFolders.Count} 个目录已存在且不在账本里，覆盖没批下来，整批中止："
                    + string.Join("、", foreignFolders);
                return false;
            }

            overwriteIds = foreignIds;
            failureReason = null;
            return true;
        }

        /// <summary>把批下来要顶掉的那批并进先删名单，已在前面的不重复删。</summary>
        private static List<string> MergeIds(IReadOnlyList<string> first, IReadOnlyList<string> second)
        {
            var merged = new List<string>();
            for (int i = 0; first != null && i < first.Count; i++)
            {
                if (!string.IsNullOrEmpty(first[i])) merged.Add(first[i]);
            }

            for (int i = 0; second != null && i < second.Count; i++)
            {
                if (!string.IsNullOrEmpty(second[i]) && !merged.Contains(second[i])) merged.Add(second[i]);
            }

            return merged;
        }

        /// <summary>
        /// 把这一批插件从检出复制到 <c>Packages/</c>，并补一份 Unity 认的 <c>package.json</c>。
        /// </summary>
        /// <remarks>复制而不是移动：检出留在 Library 作下一轮的增量源；未勾选的目录不复制。</remarks>
        private bool CopyInstalledFolders(InstallPlan plan, out string failureReason)
        {
            for (int i = 0; i < plan.ToInstall.Count; i++)
            {
                PluginManifest manifest = plan.ToInstall[i];
                string source = Path.Combine(_sourceRootPath, manifest.id);
                if (!Directory.Exists(source))
                {
                    failureReason = $"{manifest.id} 没落到检出里，先刷新再试";
                    return false;
                }

                string target = InstallFolderPath(manifest.id);
                try
                {
                    CopyDirectory(source, target);
                    WriteEmbeddedPackageManifest(target, manifest);
                }
                catch (Exception exception)
                {
                    failureReason = $"{manifest.id} 落到 Packages 失败：{exception.Message}";
                    return false;
                }
            }

            failureReason = null;
            return true;
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);

            string[] directories = Directory.GetDirectories(source, "*", SearchOption.AllDirectories);
            for (int i = 0; i < directories.Length; i++)
            {
                Directory.CreateDirectory(Path.Combine(target, RelativePath(source, directories[i])));
            }

            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string destination = Path.Combine(target, RelativePath(source, files[i]));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(files[i], destination, true);
            }
        }

        private static string RelativePath(string root, string fullPath)
        {
            return fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// 写安装目录的 <c>package.json</c>：Unity 只把带清单的 Packages 子目录当嵌入包。
        /// </summary>
        /// <remarks>包名与目录名同值（<c>&lt;检出目录名&gt;.&lt;id&gt;</c>）；插件分支里那份清单名固定为 <c>&lt;id&gt;.plugin.json</c>，两者互不替代。</remarks>
        private static void WriteEmbeddedPackageManifest(string folder, PluginManifest manifest)
        {
            var data = new EmbeddedPackageManifestData
            {
                name = new DirectoryInfo(folder).Name,
                displayName = string.IsNullOrEmpty(manifest.name) ? manifest.id : manifest.name,
                version = manifest.version,
                description = manifest.description ?? string.Empty,
                author = manifest.author ?? string.Empty,
                unity = UnityFloor(manifest.minUnityVersion)
            };

            File.WriteAllText(Path.Combine(folder, ROOT_PACKAGE_JSON_NAME), JsonUtility.ToJson(data, true));
        }

        private static string UnityFloor(string minUnityVersion)
        {
            return PluginVersion.TryParse(minUnityVersion, out PluginVersion version)
                ? version.Major + "." + version.Minor
                : minUnityVersion;
        }

        /// <summary>
        /// 只读校验分支根的 package.json：名字与检出目录名相符，工具永不写它。
        /// </summary>
        private bool ValidateRootPackageJson(PluginCatalogResult catalog, Dictionary<string, string> textsByPath,
            PluginInstallReport report, out string failureReason)
        {
            if (!textsByPath.TryGetValue(ROOT_PACKAGE_JSON_NAME, out string json)
                && !_source.TryReadBlob(ROOT_PACKAGE_JSON_NAME, out json, out failureReason))
            {
                return false;
            }

            var root = JsonUtility.ToObject<RootPackageJsonData>(json);
            string expected = new DirectoryInfo(_sourceRootPath).Name;
            if (root == null || !string.Equals(root.name, expected, StringComparison.Ordinal))
            {
                failureReason = $"分支根 package.json 的 name '{root?.name}' 与检出目录名 '{expected}' 不符";
                return false;
            }

            report.Note($"分支根包名校验通过：{root.name}");
            failureReason = null;
            return true;
        }

        /// <summary>
        /// 逐个新装目录验 .asmdef 存在且装配名不与工程内任何装配重名。
        /// </summary>
        private bool ValidateAssemblyDefinitions(InstallPlan plan, List<string> selectedIds, PluginInstallReport report,
            out string failureReason)
        {
            var taken = CollectAssemblyNames(selectedIds, out string scanReason);
            if (taken == null)
            {
                failureReason = scanReason;
                return false;
            }

            foreach (PluginManifest manifest in plan.ToInstall)
            {
                string folder = Path.Combine(_sourceRootPath, manifest.id);
                if (!Directory.Exists(folder))
                {
                    failureReason = $"{manifest.id} 勾选后没落到检出里，先刷新再试";
                    return false;
                }

                string[] files = Directory.GetFiles(folder, "*.asmdef", SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    failureReason = $"{manifest.id} 没有 .asmdef，装进来不会被编译";
                    return false;
                }

                for (int i = 0; i < files.Length; i++)
                {
                    if (!TryReadAssemblyName(files[i], out string assemblyName, out string readReason))
                    {
                        failureReason = readReason;
                        return false;
                    }

                    if (taken.Contains(assemblyName))
                    {
                        failureReason = $"{manifest.id} 的装配名 '{assemblyName}' 与工程内已有装配重名";
                        return false;
                    }

                    taken.Add(assemblyName);
                    report.Note($"{manifest.id} 装配校验通过：{assemblyName}");
                }
            }

            failureReason = string.Empty;
            return true;
        }

        private HashSet<string> CollectAssemblyNames(List<string> exemptedIds, out string failureReason)
        {
            failureReason = null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var exemptedFolders = new List<string>();
            for (int i = 0; i < exemptedIds.Count; i++) exemptedFolders.Add(InstallFolderPath(exemptedIds[i]));

            string packagesRoot = _installRootPath;
            string assetsRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets");

            foreach (string root in new[] { packagesRoot, assetsRoot })
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                string[] files = Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories);

                for (int i = 0; i < files.Length; i++)
                {
                    if (IsUnderAny(files[i], exemptedFolders)) continue;
                    if (!TryReadAssemblyName(files[i], out string name, out failureReason)) return null;
                    names.Add(name);
                }
            }

            return names;
        }

        private static bool IsUnderAny(string fullPath, List<string> folders)
        {
            string normalized = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar);
            for (int i = 0; i < folders.Count; i++)
            {
                string folder = Path.GetFullPath(folders[i]).TrimEnd(Path.DirectorySeparatorChar);
                if (normalized.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static bool TryReadAssemblyName(string asmdefPath, out string assemblyName, out string failureReason)
        {
            assemblyName = null;
            try
            {
                var definition = JsonUtility.ToObject<AssemblyDefinitionNameData>(File.ReadAllText(asmdefPath));
                if (definition != null && !string.IsNullOrEmpty(definition.name))
                {
                    assemblyName = definition.name;
                    failureReason = null;
                    return true;
                }

                failureReason = $"{asmdefPath} 里没有 name 字段";
                return false;
            }
            catch (Exception exception)
            {
                failureReason = $"{asmdefPath} 读不出装配名：{exception.Message}";
                return false;
            }
        }

        private string ReadProjectManifest(out string failureReason)
        {
            if (!File.Exists(_projectManifestPath))
            {
                failureReason = $"工程 manifest 不在：{_projectManifestPath}";
                return null;
            }

            failureReason = null;
            return File.ReadAllText(_projectManifestPath);
        }

        /// <summary>整文替换工程 manifest：先落临时名再覆盖改名，避免半篇文件被 Unity 抢先读到。</summary>
        /// <remarks>不用 <c>File.Move(source, target, overwrite)</c>：那个重载在最低引擎版本的 BCL 面里没有。</remarks>
        private void WriteProjectManifest(string text)
        {
            string temporary = _projectManifestPath + ".moirai-tmp";
            File.WriteAllText(temporary, text);

            try
            {
                File.Copy(temporary, _projectManifestPath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <summary>
        /// 带基线核对的写入：读进来与此刻盘上不一致说明别人动过，让路不覆盖。
        /// </summary>
        private bool TryWriteProjectManifest(string baseline, string text, out string failureReason)
        {
            string current = ReadProjectManifest(out failureReason);
            if (current == null) return false;

            if (!string.Equals(current, baseline, StringComparison.Ordinal))
            {
                failureReason = $"工程 manifest 在本次操作期间被别处改过：{_projectManifestPath}";
                return false;
            }

            WriteProjectManifest(text);
            failureReason = null;
            return true;
        }

        /// <summary>
        /// 按台账逐条记归属并写盘，任一步失败都不落盘。
        /// </summary>
        private bool WriteProjectManifest(PackageManagerStateData state, InstallPlan plan, PluginInstallReport report,
            out string failureReason)
        {
            string baseline = ReadProjectManifest(out string readReason);
            if (readReason != null)
            {
                failureReason = readReason;
                return false;
            }

            string text = baseline;
            bool changed = false;
            foreach (PluginManifest manifest in plan.ToInstall)
            {
                PluginUpmDependency[] dependencies = manifest.upmDependencies;
                for (int i = 0; dependencies != null && i < dependencies.Length; i++)
                {
                    UpmEntryOutcome outcome = _stateRegistry.RecordUpmOwner(state, text, dependencies[i],
                        manifest.id, out string written);
                    if (!ReferenceEquals(written, text))
                    {
                        text = written;
                        changed = true;
                    }

                    report.Note($"外部依赖 {dependencies[i].id}：{outcome}");
                }
            }

            if (changed && !TryWriteProjectManifest(baseline, text, out string writeReason))
            {
                failureReason = writeReason;
                return false;
            }

            failureReason = null;
            return true;
        }

        /// <summary>
        /// 只减掉没有别的使用者的宏，成对回收。
        /// </summary>
        private static void RemoveOrphanTags(InstalledPluginData entry, HashSet<string> stillClaimed,
            PluginInstallReport report)
        {
            string[] tags = entry.tags;
            if (tags == null || tags.Length == 0) return;

            for (int i = 0; i < tags.Length; i++)
            {
                if (string.IsNullOrEmpty(tags[i])) continue;

                if (stillClaimed.Contains(tags[i]))
                {
                    report.Note($"宏 {tags[i]} 还有别的使用者，留着");
                    continue;
                }

                ScriptingDefineSymbols.RemoveScriptingDefineSymbol(tags[i]);
                report.Note($"撤掉宏 {tags[i]}");
            }
        }

        [Serializable]
        private sealed class RootPackageJsonData
        {
            public string name;
        }

        [Serializable]
        private sealed class EmbeddedPackageManifestData
        {
            public string name;
            public string displayName;
            public string version;
            public string description;
            public string author;
            public string unity;
        }

        [Serializable]
        private sealed class AssemblyDefinitionNameData
        {
            public string name;
        }
    }
}
