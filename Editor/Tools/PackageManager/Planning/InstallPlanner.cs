using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 依赖闭包与安装顺序的规划面。
    /// </summary>
    /// <remarks>
    /// 纯函数：只吃清单集合与标识集合，不碰 git 也不碰磁盘，全部判据都能在无宿主的情况下覆盖。<br />
    /// 版本判定只看清单里那份候选——已装记录只有标识集合，拿不到已装版本号，因此"已装且未强制"仅按在场复用。<br />
    /// 冲突一律整批中止，不做"跳过坏边继续装"的降级。
    /// </remarks>
    internal static class InstallPlanner
    {
        /// <summary>规划一次安装，依赖排在依赖方之前。</summary>
        /// <remarks><paramref name="frameworkVersion"/> 以 <c>0.0.0</c>（<c>default</c>）作「未提供」哨兵：给了才判框架地板，纯规划用例可以不传；生产路径必须传当前框架版本的实测值。</remarks>
        internal static InstallPlan Plan(IReadOnlyList<PluginManifest> catalog, IReadOnlyCollection<string> installedIds,
            IReadOnlyCollection<string> requestedIds, IReadOnlyCollection<string> forceIds, PluginVersion unityVersion,
            PluginVersion frameworkVersion = default)
        {
            var resolution = new Closure(catalog, installedIds, forceIds, unityVersion, frameworkVersion);
            resolution.Run(requestedIds);
            return resolution.Plan;
        }

        /// <summary>
        /// 列出在场且仍依赖该插件的已装插件，直接依赖方排在传递依赖方之前。
        /// </summary>
        internal static IReadOnlyList<string> DependentsOf(IReadOnlyList<PluginManifest> catalog, string id,
            IReadOnlyCollection<string> installedIds)
        {
            var dependents = new List<string>();
            if (catalog == null || string.IsNullOrEmpty(id)) return dependents;

            HashSet<string> installed = ToSet(installedIds);
            if (installed.Count == 0) return dependents;

            // 反向逐层扩散：队列里每一项都是已确认的依赖方，因此自环与重复计入会被 seen 挡下。
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var frontier = new List<string> { id };
            for (int head = 0; head < frontier.Count; head++)
            {
                string dependency = frontier[head];
                foreach (PluginManifest candidate in catalog)
                {
                    if (candidate == null || !installed.Contains(candidate.id)) continue;
                    if (!DependsOn(candidate, dependency)) continue;
                    if (!seen.Add(candidate.id)) continue;

                    dependents.Add(candidate.id);
                    frontier.Add(candidate.id);
                }
            }

            return dependents;
        }

        private static bool DependsOn(PluginManifest candidate, string dependencyId)
        {
            PluginDependency[] edges = candidate.dependencies;
            if (edges == null) return false;

            for (int i = 0; i < edges.Length; i++)
            {
                if (edges[i] != null && string.Equals(edges[i].id, dependencyId, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static HashSet<string> ToSet(IReadOnlyCollection<string> source)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (source == null) return set;

            foreach (string entry in source)
            {
                if (!string.IsNullOrEmpty(entry)) set.Add(entry);
            }

            return set;
        }

        /// <summary>
        /// 一趟规划的可变状态：共享一份解析缓存，跨请求根合并冲突。
        /// </summary>
        private sealed class Closure
        {
            private readonly Dictionary<string, PluginManifest> _byId;
            private readonly HashSet<string> _installed;
            private readonly HashSet<string> _forced;
            private readonly HashSet<string> _resolved;
            private readonly HashSet<string> _planned;
            private readonly List<string> _visiting;
            private readonly PluginVersion _unityVersion;
            private readonly PluginVersion _frameworkVersion;

            internal Closure(IReadOnlyList<PluginManifest> catalog, IReadOnlyCollection<string> installedIds,
                IReadOnlyCollection<string> forceIds, PluginVersion unityVersion, PluginVersion frameworkVersion)
            {
                Plan = new InstallPlan();
                _byId = new Dictionary<string, PluginManifest>(StringComparer.Ordinal);
                _installed = ToSet(installedIds);
                _forced = ToSet(forceIds);
                _resolved = new HashSet<string>(StringComparer.Ordinal);
                _planned = new HashSet<string>(StringComparer.Ordinal);
                _visiting = new List<string>();
                _unityVersion = unityVersion;
                _frameworkVersion = frameworkVersion;

                if (catalog == null) return;
                foreach (PluginManifest manifest in catalog)
                {
                    if (manifest == null || string.IsNullOrEmpty(manifest.id)) continue;
                    _byId[manifest.id] = manifest;
                }
            }

            internal InstallPlan Plan { get; }

            internal void Run(IReadOnlyCollection<string> requestedIds)
            {
                if (requestedIds != null)
                {
                    foreach (string id in requestedIds) Visit(id, null, null);
                }

                if (Plan.HasConflicts)
                {
                    Plan.AbortBatch();
                }
            }

            private bool Visit(string id, PluginManifest dependent, PluginDependency requirement)
            {
                if (!_byId.TryGetValue(id, out PluginManifest candidate))
                {
                    string owner = dependent == null ? id : dependent.id;
                    string reason = dependent == null
                        ? $"找不到插件 '{id}'"
                        : $"{dependent.id} 依赖 '{id}'，分支目录里没有该插件";
                    Plan.AddConflict(new PlanConflict(owner, PlanConflictKind.MissingDependency, reason));
                    return false;
                }

                if (!CheckVersionRequirement(candidate, dependent, requirement)) return false;
                if (!CheckUnityFloor(candidate)) return false;
                if (!CheckFrameworkFloor(candidate)) return false;

                if (_resolved.Contains(id)) return true;
                if (_visiting.Contains(id))
                {
                    Plan.AddConflict(new PlanConflict(candidate.id, PlanConflictKind.Cycle, DescribeCycle(id)));
                    return false;
                }

                _visiting.Add(id);
                bool clean = true;
                PluginDependency[] edges = candidate.dependencies;
                if (edges != null)
                {
                    for (int i = 0; i < edges.Length; i++)
                    {
                        PluginDependency edge = edges[i];
                        if (edge == null || string.IsNullOrEmpty(edge.id)) continue;

                        // 不短路：同一层的多条冲突要一次报全，否则修一条才露下一条。
                        if (!Visit(edge.id, candidate, edge)) clean = false;
                    }
                }

                _visiting.RemoveAt(_visiting.Count - 1);
                if (!clean) return false;

                _resolved.Add(id);
                if (!_planned.Contains(id) && (!_installed.Contains(id) || _forced.Contains(id)))
                {
                    _planned.Add(id);
                    Plan.AddInstall(candidate);
                }

                return true;
            }

            private bool CheckVersionRequirement(PluginManifest candidate, PluginManifest dependent, PluginDependency requirement)
            {
                if (requirement == null) return true;
                PluginVersion.TryParse(candidate.version, out PluginVersion available);
                if (requirement.Matches(available)) return true;

                string have = string.IsNullOrEmpty(candidate.version) ? "<缺 version>" : candidate.version;
                Plan.AddConflict(new PlanConflict(dependent.id, PlanConflictKind.VersionUnsatisfied,
                    $"{dependent.id} 要求 {requirement.id} {requirement.versionOperator}{requirement.version}，分支上是 {have}"));
                return false;
            }

            private bool CheckUnityFloor(PluginManifest candidate)
            {
                if (candidate.SatisfiesUnityVersion(_unityVersion)) return true;

                Plan.AddConflict(new PlanConflict(candidate.id, PlanConflictKind.UnityFloorUnmet,
                    $"{candidate.id} 需要 Unity {candidate.minUnityVersion} 及以上，当前编辑器是 {_unityVersion}"));
                return false;
            }

            private bool CheckFrameworkFloor(PluginManifest candidate)
            {
                // 未提供框架版本（0.0.0 哨兵）就不判这条地板：纯规划用例可以不传，生产路径必传实测值
                if (_frameworkVersion == default) return true;
                if (candidate.SatisfiesFrameworkVersion(_frameworkVersion)) return true;

                Plan.AddConflict(new PlanConflict(candidate.id, PlanConflictKind.FrameworkFloorUnmet,
                    $"{candidate.id} 需要框架 {candidate.minFrameworkVersion} 及以上，当前框架是 {_frameworkVersion}"));
                return false;
            }

            private string DescribeCycle(string id)
            {
                var parts = new List<string>();
                for (int i = Math.Max(0, _visiting.IndexOf(id)); i < _visiting.Count; i++) parts.Add(_visiting[i]);
                parts.Add(id);
                return "依赖成环：" + string.Join(" → ", parts);
            }
        }
    }
}
