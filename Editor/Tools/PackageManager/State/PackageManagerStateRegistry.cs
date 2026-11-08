using System;
using System.Collections.Generic;
using System.IO;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 包管理器已装状态与 UPM 依赖归属的账本。
    /// </summary>
    /// <remarks>
    /// 一个实例绑定一份状态文件与一套缺省设置；不同包管理器各持一份，互不覆盖。<br />
    /// 落盘位置由调用方给定（默认在 <c>ProjectSettings/</c> 下）；卸载与回收的全部依据都来自这里，不靠扫描工作树反推。<br />
    /// 记账项一律按 <c>id</c> / <c>packageId</c> 去重（<see cref="UpsertInstalled"/>、<see cref="RecordUpmOwner"/>），重复写入覆盖旧值而非追加。<br />
    /// 读到损坏的状态文件时把原文另存为 <c>.corrupt-&lt;ticks&gt;</c> 再回空态，绝不就地覆写。<br />
    /// 安装状态必须在 <c>AssetDatabase.Refresh()</c> 之前落盘——刷新触发的域重载会杀掉在飞续体。
    /// </remarks>
    internal sealed class PackageManagerStateRegistry
    {
        private readonly string _statePath;
        private readonly Func<PackageManagerSettingsData> _createDefaultSettings;

        /// <summary>
        /// 建一份绑定到指定状态文件的账本；缺省设置由调用方按各自的包源给出。
        /// </summary>
        internal PackageManagerStateRegistry(string statePath, Func<PackageManagerSettingsData> createDefaultSettings)
        {
            if (string.IsNullOrEmpty(statePath)) throw new ArgumentException("状态文件路径不能为空", nameof(statePath));

            _statePath = Path.GetFullPath(statePath);
            _createDefaultSettings = createDefaultSettings;
        }

        /// <summary>
        /// 工程默认状态文件路径：<c>ProjectSettings/&lt;fileName&gt;</c>。
        /// </summary>
        internal static string ResolveDefaultPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("状态文件名不能为空", nameof(fileName));
            return Path.Combine(Directory.GetCurrentDirectory(), "ProjectSettings", fileName);
        }

        /// <summary>当前状态文件的落盘路径。</summary>
        internal string StatePath => _statePath;

        /// <summary>
        /// 读入账本；文件缺失或损坏时回默认空态。
        /// </summary>
        internal PackageManagerStateData Load()
        {
            TryLoad(out PackageManagerStateData state, out _);
            return state;
        }

        /// <summary>
        /// 读入账本并交出失败原因。
        /// </summary>
        /// <remarks>账本自身不记日志：本工具直接发 <c>Debug.LogError</c> 会把 EditMode 用例打成未处理日志红，
        /// 且报告属交互面——原因交给调用方在窗口上显示。</remarks>
        internal bool TryLoad(out PackageManagerStateData state, out string failureReason)
        {
            failureReason = null;
            if (!File.Exists(_statePath))
            {
                state = CreateDefault();
                return true;
            }

            string text = File.ReadAllText(_statePath);
            if (string.IsNullOrEmpty(text))
            {
                state = CreateDefault();
                return true;
            }

            PackageManagerStateData parsed;
            try
            {
                parsed = JsonUtility.ToObject<PackageManagerStateData>(text);
            }
            catch (Exception exception)
            {
                string preserveFailure = PreserveCorrupt(_statePath);
                failureReason = $"状态文件解析失败，已另存副本：{exception.Message}{preserveFailure}";
                state = CreateDefault();
                return false;
            }

            if (parsed == null)
            {
                failureReason = "状态文件解析结果为空";
                state = CreateDefault();
                return false;
            }

            ApplyDefaults(parsed);
            state = parsed;
            return true;
        }

        /// <summary>
        /// 把上次刷新的时刻记进设置项。
        /// </summary>
        internal void StampFetch(PackageManagerStateData state)
        {
            if (state?.settings == null) return;
            state.settings.lastFetchAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 把一趟扫描的结果记成缓存（连当时的远端与分支，换源后即作废）。
        /// </summary>
        internal void CacheCatalog(PackageManagerStateData state, PluginCatalogResult catalog, string headCommit)
        {
            if (state?.settings == null || catalog == null) return;

            PackageManagerCatalogCache cache = state.catalogCache;
            if (cache == null) state.catalogCache = cache = new PackageManagerCatalogCache();

            cache.repoUrl = state.settings.repoUrl;
            cache.branch = state.settings.branch;
            cache.headCommit = headCommit;
            cache.manifests.Clear();
            for (int i = 0; i < catalog.Manifests.Count; i++) cache.manifests.Add(catalog.Manifests[i]);
        }

        /// <summary>
        /// 取与当前设置匹配的上次扫描缓存；没有、为空或换过源都交出 false。
        /// </summary>
        internal bool TryGetCachedCatalog(PackageManagerStateData state, out List<PluginManifest> manifests,
            out string headCommit)
        {
            manifests = null;
            headCommit = null;

            PackageManagerCatalogCache cache = state?.catalogCache;
            if (cache?.manifests == null || cache.manifests.Count == 0) return false;
            if (!string.Equals(cache.repoUrl, state.settings?.repoUrl, StringComparison.Ordinal)) return false;
            if (!string.Equals(cache.branch, state.settings?.branch, StringComparison.Ordinal)) return false;

            var alive = new List<PluginManifest>(cache.manifests.Count);
            for (int i = 0; i < cache.manifests.Count; i++)
            {
                if (cache.manifests[i] != null) alive.Add(cache.manifests[i]);
            }

            if (alive.Count == 0) return false;

            manifests = alive;
            headCommit = cache.headCommit;
            return true;
        }

        /// <summary>
        /// 写入账本：先落临时名再覆盖，不留半篇文件。
        /// </summary>
        /// <remarks>就地整写被中断会留下截断的 JSON，而读侧对损坏文件回的是空账本——那等于"什么都没装"，
        /// 盘上的插件目录却还在。不用带 overwrite 的 <c>File.Move</c>：那个重载在最低引擎版本的 BCL 面里没有。</remarks>
        internal void Save(PackageManagerStateData state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            EnsureLists(state);

            string directory = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temporary = _statePath + ".moirai-tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true));

            try
            {
                File.Copy(temporary, _statePath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <summary>
        /// 写入或覆盖一个已装记录。
        /// </summary>
        internal void UpsertInstalled(PackageManagerStateData state, InstalledPluginData entry)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (entry == null || string.IsNullOrEmpty(entry.id)) throw new ArgumentException("缺 id 的已装记录不入账", nameof(entry));

            EnsureLists(state);

            for (int i = 0; i < state.installed.Count; i++)
            {
                if (!string.Equals(state.installed[i].id, entry.id, StringComparison.Ordinal)) continue;
                state.installed[i] = entry;
                return;
            }

            state.installed.Add(entry);
        }

        /// <summary>
        /// 移出一个已装记录。
        /// </summary>
        internal bool RemoveInstalled(PackageManagerStateData state, string id)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            EnsureLists(state);

            for (int i = 0; i < state.installed.Count; i++)
            {
                if (!string.Equals(state.installed[i].id, id, StringComparison.Ordinal)) continue;
                state.installed.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 取已装记录。
        /// </summary>
        internal InstalledPluginData FindInstalled(PackageManagerStateData state, string id)
        {
            if (state?.installed == null) return null;

            for (int i = 0; i < state.installed.Count; i++)
            {
                if (string.Equals(state.installed[i].id, id, StringComparison.Ordinal)) return state.installed[i];
            }

            return null;
        }

        /// <summary>
        /// 记下一个使用者并按归属规则写工程 manifest。
        /// </summary>
        internal UpmEntryOutcome RecordUpmOwner(PackageManagerStateData state, string manifestText,
            PluginUpmDependency dependency, string ownerPluginId, out string newText)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            newText = manifestText;
            if (dependency == null || string.IsNullOrEmpty(dependency.id)) return UpmEntryOutcome.NotFound;
            if (string.IsNullOrEmpty(manifestText)) return UpmEntryOutcome.NotFound;

            EnsureLists(state);

            UpmOwnershipData entry = FindOwnership(state, dependency.id);
            if (entry != null)
            {
                AddOwner(entry, ownerPluginId);

                if (entry.preexisting) return UpmEntryOutcome.KeptUserEntry;
                if (IsAtLeast(entry.requiredVersion, dependency.version)) return UpmEntryOutcome.Retained;

                newText = ProjectManifestUtility.SetDependency(manifestText, dependency.id, dependency.version,
                    takeOverExisting: true, out _);
                entry.requiredVersion = dependency.version;
                return UpmEntryOutcome.Updated;
            }

            bool existsOnDisk = ProjectManifestUtility.TryGetDependency(manifestText, dependency.id, out string current);

            var created = new UpmOwnershipData
            {
                packageId = dependency.id,
                requiredVersion = dependency.version,
                preexisting = existsOnDisk,
                originalValue = existsOnDisk ? current : null
            };
            AddOwner(created, ownerPluginId);
            state.upmOwnership.Add(created);

            if (existsOnDisk) return UpmEntryOutcome.KeptUserEntry;

            newText = ProjectManifestUtility.SetDependency(manifestText, dependency.id, dependency.version,
                takeOverExisting: false, out _);
            return UpmEntryOutcome.Added;
        }

        /// <summary>
        /// 记下一个使用者离场，并按引用计数回收条目。
        /// </summary>
        internal UpmEntryOutcome ReleaseUpmOwner(PackageManagerStateData state, string manifestText,
            string packageId, string ownerPluginId, out string newText)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            newText = manifestText;

            EnsureLists(state);

            UpmOwnershipData entry = FindOwnership(state, packageId);
            if (entry == null) return UpmEntryOutcome.NotFound;

            // 归属记录里没有它，就既不该撤账也不该删行：半路接手或手改过的账本不该被无关卸载清掉
            if (entry.owners == null || !ContainsOwner(entry.owners, ownerPluginId)) return UpmEntryOutcome.NotFound;

            for (int i = entry.owners.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entry.owners[i], ownerPluginId, StringComparison.Ordinal)) entry.owners.RemoveAt(i);
            }

            if (entry.owners.Count > 0) return UpmEntryOutcome.Retained;

            // 用户手写的条目即使再无使用者也原样留着
            if (entry.preexisting)
            {
                state.upmOwnership.Remove(entry);
                return UpmEntryOutcome.KeptUserEntry;
            }

            state.upmOwnership.Remove(entry);
            newText = ProjectManifestUtility.RemoveDependency(manifestText, packageId, out _);
            return UpmEntryOutcome.Removed;
        }

        /// <summary>
        /// 取某外部包的归属记录；窗口标"谁在用/是不是用户手写"走这里。
        /// </summary>
        internal UpmOwnershipData FindOwnership(PackageManagerStateData state, string packageId)
        {
            if (state.upmOwnership == null) return null;

            for (int i = 0; i < state.upmOwnership.Count; i++)
            {
                if (string.Equals(state.upmOwnership[i].packageId, packageId, StringComparison.Ordinal)) return state.upmOwnership[i];
            }

            return null;
        }

        private static string PreserveCorrupt(string path)
        {
            try
            {
                File.Copy(path, $"{path}.corrupt-{DateTime.UtcNow.Ticks}", true);
                return null;
            }
            catch (Exception exception)
            {
                return $"，且损坏副本未能保存：{exception.Message}";
            }
        }

        private PackageManagerStateData CreateDefault()
        {
            var state = new PackageManagerStateData();
            EnsureLists(state);
            return state;
        }

        /// <summary>
        /// 把缺项补成这套包源的缺省值；手改过或旧版的账本不该因缺一项就整份不可用。
        /// </summary>
        private void ApplyDefaults(PackageManagerStateData state)
        {
            EnsureLists(state);

            PackageManagerSettingsData defaults = CreateDefaultSettings();
            if (string.IsNullOrEmpty(state.settings.branch)) state.settings.branch = defaults.branch;
        }

        private PackageManagerSettingsData CreateDefaultSettings()
        {
            return _createDefaultSettings?.Invoke() ?? new PackageManagerSettingsData();
        }

        private void EnsureLists(PackageManagerStateData state)
        {
            if (state.settings == null) state.settings = CreateDefaultSettings();
            if (state.installed == null) state.installed = new List<InstalledPluginData>();
            if (state.upmOwnership == null) state.upmOwnership = new List<UpmOwnershipData>();
        }

        private static bool ContainsOwner(List<string> owners, string ownerPluginId)
        {
            if (string.IsNullOrEmpty(ownerPluginId)) return false;

            for (int i = 0; i < owners.Count; i++)
            {
                if (string.Equals(owners[i], ownerPluginId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static void AddOwner(UpmOwnershipData entry, string ownerPluginId)
        {
            if (string.IsNullOrEmpty(ownerPluginId)) return;
            if (entry.owners == null) entry.owners = new List<string>();

            for (int i = 0; i < entry.owners.Count; i++)
            {
                if (string.Equals(entry.owners[i], ownerPluginId, StringComparison.Ordinal)) return;
            }

            entry.owners.Add(ownerPluginId);
        }

        private static bool IsAtLeast(string recorded, string required)
        {
            if (!PluginVersion.TryParse(recorded, out PluginVersion have)) return false;
            if (!PluginVersion.TryParse(required, out PluginVersion want)) return true;
            return have >= want;
        }
    }
}
