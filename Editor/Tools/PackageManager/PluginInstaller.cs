using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 装卸编排：把包源层、发现层、规划层与账本按固定时序串起来。
    /// </summary>
    /// <remarks>
    /// 时序：建检出 → 摆正分支 → fetch → 再摆正（fetch 后本地分支才看得见新提交）→ 列树 → 建目录 → 规划，
    /// 规划判出冲突即整批中止，此刻还没写过任何东西。<br />
    /// 落盘顺序收在"先写工程 manifest 与脚本宏，再把状态账本落盘，最后只刷新一次"：刷新会触发域重载，
    /// 账本晚一步就没机会写完。<br />
    /// 卸载的依据全部来自账本：分支上那份清单消失了也要能回收，不靠扫描工作树反推。
    /// </remarks>
    internal sealed class PluginInstaller
    {
        private const string RootPackageJsonName = "package.json";
        private const int EngineVersionSegmentCount = 3;

        private readonly string _packageRootPath;
        private readonly string _projectManifestPath;
        private readonly PluginPackageSource _source;
        private readonly PackageManagerSettingsData _settings;

        internal PluginInstaller(PackageManagerSettingsData settings, string packageRootPath, string projectManifestPath)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _settings = settings;
            _packageRootPath = Path.GetFullPath(packageRootPath);
            _projectManifestPath = Path.GetFullPath(projectManifestPath);
            _source = new PluginPackageSource(_packageRootPath, settings.repoUrl, settings.branch, settings.gitPath);
        }

        /// <summary>按工程布局建一个编排器：落点与工程 manifest 都在 Packages 下。</summary>
        internal static PluginInstaller Create(PackageManagerSettingsData settings)
        {
            string packagesRoot = Path.Combine(Directory.GetCurrentDirectory(), "Packages");
            return new PluginInstaller(settings,
                Path.Combine(packagesRoot, "com.moirai.framework.plugin"),
                Path.Combine(packagesRoot, "manifest.json"));
        }

        /// <summary>包源层，窗口取源状态用。</summary>
        internal PluginPackageSource Source => _source;

        /// <summary>插件落点目录。</summary>
        internal string PackageRootPath => _packageRootPath;

        /// <summary>取当前编辑器版本，形状不符时回最低地板并交出原因。</summary>
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

        /// <summary>从引擎版本号里取前三段十进制。</summary>
        /// <remarks>修订段常带发行后缀（<c>2022.3.62f3</c>、<c>6000.3.26f1</c>），只取它的前导数字。</remarks>
        internal static bool TryParseEngineVersion(string engineVersion, out PluginVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(engineVersion)) return false;

            string[] parts = engineVersion.Split('.');
            if (parts.Length < EngineVersionSegmentCount) return false;
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

        /// <summary>走一遍源侧读取，产出目录与分支头。</summary>
        internal bool TryBrowse(PluginInstallReport report, out PluginCatalogResult catalog, out string headCommit)
        {
            catalog = null;
            headCommit = null;

            if (!_source.EnsureClone(out string reason))
            {
                report.Fail(reason);
                return false;
            }

            report.Note("落点工作树就绪");

            if (!_source.Fetch(out reason))
            {
                report.Fail(reason);
                return false;
            }

            report.Note($"已取 {(_settings.branch ?? string.Empty)} 分支头");

            if (!_source.EnsureBranchAttached(out reason))
            {
                report.Fail(reason);
                return false;
            }

            if (!_source.TryListPaths(out List<string> treePaths, out reason))
            {
                report.Fail(reason);
                return false;
            }

            catalog = PluginCatalog.Build(treePaths, path =>
            {
                return _source.TryReadBlob(path, out string text, out _) ? text : null;
            });

            for (int i = 0; i < catalog.Errors.Count; i++) report.Note(catalog.Errors[i]);

            if (!ValidateRootPackageJson(catalog, report, out reason))
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

        /// <summary>装一个插件，连它的依赖闭包一起。</summary>
        internal PluginInstallReport Install(string pluginId, bool force)
        {
            return Install(new[] { pluginId }, force);
        }

        /// <summary>
        /// 装一批插件：一次勾选集写入、一次 manifest 写入、一次刷新。
        /// </summary>
        /// <remarks>分批装会在每个包后各触发一次域重载，第二趟起就没人在跑了——多包必须走这里。</remarks>
        internal PluginInstallReport Install(IReadOnlyList<string> pluginIds, bool force)
        {
            var report = new PluginInstallReport();

            if (!TryGetEngineVersion(out PluginVersion unityVersion, out string versionReason)) report.Note(versionReason);
            if (!TryBrowse(report, out PluginCatalogResult catalog, out string headCommit)) return report;

            PackageManagerStateData state = PackageManagerStateRegistry.Load();
            List<string> installedIds = InstalledIds(state);

            InstallPlan plan = InstallPlanner.Plan(catalog.Manifests, installedIds, pluginIds,
                force ? pluginIds : NoIds, unityVersion);

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

            List<string> selectedIds = Union(installedIds, plan);
            if (!_source.ApplySparsePaths(selectedIds, out string sparseReason))
            {
                report.Fail(sparseReason);
                return report;
            }

            report.Note($"勾选集收到 {selectedIds.Count} 项");

            if (!ValidateAssemblyDefinitions(plan, selectedIds, report, out string asmdefReason))
            {
                report.Fail(asmdefReason);
                return report;
            }

            if (!WriteProjectManifest(state, plan, report, out string manifestReason))
            {
                report.Fail(manifestReason);
                return report;
            }

            foreach (PluginManifest manifest in plan.ToInstall)
            {
                string[] tags = manifest.tags;
                for (int i = 0; tags != null && i < tags.Length; i++) ScriptingDefineSymbols.AddScriptingDefineSymbol(tags[i]);

                PackageManagerStateRegistry.UpsertInstalled(state, new InstalledPluginData
                {
                    id = manifest.id,
                    version = manifest.version,
                    commit = headCommit,
                    function = manifest.function,
                    tags = tags ?? Array.Empty<string>()
                });

                report.AddTouched(manifest.id);
                report.Note($"装了 {manifest.id} {manifest.version}");
            }

            PackageManagerStateRegistry.Save(state);
            report.Note("状态账本已落盘");

            AssetDatabase.Refresh();
            return report;
        }

        /// <summary>卸一个插件，按账本回收外部依赖与脚本宏。</summary>
        internal PluginInstallReport Uninstall(string pluginId, bool allowCascade)
        {
            var report = new PluginInstallReport();
            PackageManagerStateData state = PackageManagerStateRegistry.Load();

            InstalledPluginData entry = PackageManagerStateRegistry.FindInstalled(state, pluginId);
            if (entry == null)
            {
                report.Fail($"账本里没有 '{pluginId}'，不凭目录猜装了什么");
                return report;
            }

            if (!TryBrowse(report, out PluginCatalogResult catalog, out _)) return report;

            IReadOnlyList<string> dependents = InstallPlanner.DependentsOf(catalog.Manifests, pluginId, InstalledIds(state));
            if (dependents.Count > 0 && !allowCascade)
            {
                for (int i = 0; i < dependents.Count; i++) report.AddDependent(dependents[i]);
                report.Fail($"仍有插件依赖它：{string.Join("、", dependents)}");
                return report;
            }

            List<string> remainingIds = InstalledIds(state);
            remainingIds.Remove(pluginId);

            if (!_source.ApplySparsePaths(remainingIds, out string sparseReason))
            {
                report.Fail(sparseReason);
                return report;
            }

            string manifestText = ReadProjectManifest(out string readReason);
            if (manifestText == null)
            {
                report.Fail(readReason);
                return report;
            }

            bool textChanged = false;
            var ownedPackageIds = new List<string>();
            for (int i = 0; i < state.upmOwnership.Count; i++) ownedPackageIds.Add(state.upmOwnership[i].packageId);

            for (int i = 0; i < ownedPackageIds.Count; i++)
            {
                UpmEntryOutcome outcome = PackageManagerStateRegistry.ReleaseUpmOwner(state, manifestText,
                    ownedPackageIds[i], pluginId, out string released);
                if (!ReferenceEquals(released, manifestText))
                {
                    manifestText = released;
                    textChanged = true;
                }

                if (outcome != UpmEntryOutcome.NotFound) report.Note($"外部依赖 {ownedPackageIds[i]}：{outcome}");
            }

            if (textChanged) WriteProjectManifest(manifestText);

            RemoveOrphanTags(entry, state, report);
            PackageManagerStateRegistry.RemoveInstalled(state, pluginId);
            PackageManagerStateRegistry.Save(state);
            report.AddTouched(pluginId);
            report.Note("状态账本已落盘");

            AssetDatabase.Refresh();
            return report;
        }

        /// <summary>判定某个已装插件在新分支头上有没有内容变化。</summary>
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

        /// <summary>取某修订下该插件目录的 tree 哈希。</summary>
        internal bool TryTreeHashOf(string pluginId, string revision, out string hash, out string failureReason)
        {
            string treePath = pluginId + "/" + pluginId + PluginManifest.ManifestSuffix;
            return _source.TryTreeHash(treePath, revision, out hash, out failureReason);
        }

        /// <summary>逐条判在场插件有没有内容变化；任一次读不通即交出原因。</summary>
        internal bool TryCollectUpdatable(PackageManagerStateData state, out List<string> updatableIds,
            out string failureReason)
        {
            updatableIds = new List<string>();
            List<string> installedIds = InstalledIds(state);

            for (int i = 0; i < installedIds.Count; i++)
            {
                InstalledPluginData entry = PackageManagerStateRegistry.FindInstalled(state, installedIds[i]);
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

        /// <summary>只读校验分支根的 package.json：名字与落点目录名相符，工具永不写它。</summary>
        private bool ValidateRootPackageJson(PluginCatalogResult catalog, PluginInstallReport report, out string failureReason)
        {
            if (!_source.TryReadBlob(RootPackageJsonName, out string json, out failureReason)) return false;

            var root = JsonUtility.ToObject<RootPackageJsonData>(json);
            string expected = new DirectoryInfo(_packageRootPath).Name;
            if (root == null || !string.Equals(root.name, expected, StringComparison.Ordinal))
            {
                failureReason = $"分支根 package.json 的 name '{root?.name}' 与落点目录名 '{expected}' 不符";
                return false;
            }

            report.Note($"分支根包名校验通过：{root.name}");
            failureReason = null;
            return true;
        }

        /// <summary>逐个新装目录验 .asmdef 存在且装配名不与工程内任何装配重名。</summary>
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
                string folder = Path.Combine(_packageRootPath, manifest.id);
                if (!Directory.Exists(folder))
                {
                    failureReason = $"{manifest.id} 勾选后没落盘，先刷新再试";
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
            for (int i = 0; i < exemptedIds.Count; i++) exemptedFolders.Add(Path.Combine(_packageRootPath, exemptedIds[i]));

            string packagesRoot = Path.GetDirectoryName(_packageRootPath);
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

        private void WriteProjectManifest(string text) => File.WriteAllText(_projectManifestPath, text);

        /// <summary>按台账逐条记归属并写盘，任一步失败都不落盘。</summary>
        private bool WriteProjectManifest(PackageManagerStateData state, InstallPlan plan, PluginInstallReport report,
            out string failureReason)
        {
            string text = ReadProjectManifest(out string readReason);
            if (readReason != null)
            {
                failureReason = readReason;
                return false;
            }

            bool changed = false;
            foreach (PluginManifest manifest in plan.ToInstall)
            {
                PluginUpmDependency[] dependencies = manifest.upmDependencies;
                for (int i = 0; dependencies != null && i < dependencies.Length; i++)
                {
                    UpmEntryOutcome outcome = PackageManagerStateRegistry.RecordUpmOwner(state, text, dependencies[i],
                        manifest.id, out string written);
                    if (!ReferenceEquals(written, text))
                    {
                        text = written;
                        changed = true;
                    }

                    report.Note($"外部依赖 {dependencies[i].id}：{outcome}");
                }
            }

            if (changed) WriteProjectManifest(text);
            failureReason = null;
            return true;
        }

        /// <summary>只减掉没有别的使用者的宏，成对回收。</summary>
        private static void RemoveOrphanTags(InstalledPluginData entry, PackageManagerStateData state,
            PluginInstallReport report)
        {
            string[] tags = entry.tags;
            if (tags == null || tags.Length == 0) return;

            var stillClaimed = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < state.installed.Count; i++)
            {
                InstalledPluginData other = state.installed[i];
                if (other == entry || string.Equals(other.id, entry.id, StringComparison.Ordinal)) continue;

                string[] otherTags = other.tags;
                for (int j = 0; otherTags != null && j < otherTags.Length; j++) stillClaimed.Add(otherTags[j]);
            }

            for (int i = 0; i < tags.Length; i++)
            {
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
        private sealed class AssemblyDefinitionNameData
        {
            public string name;
        }
    }
}
