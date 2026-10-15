using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 一次规划的结果：安装顺序、冲突与合并后的外部依赖。
    /// </summary>
    /// <remarks>
    /// <see cref="ToInstall"/> 已是依赖先行的顺序，编排层直接逐包落稀疏检出。<br />
    /// 任一冲突都会走 <see cref="AbortBatch"/>：冲突在场时安装清单与合并依赖一律清空，
    /// 让"整批中止"成为类型层面的事实，而不是靠调用方记得判一次。
    /// </remarks>
    internal sealed class InstallPlan
    {
        private readonly List<PluginManifest> _toInstall = new List<PluginManifest>();
        private readonly List<PlanConflict> _conflicts = new List<PlanConflict>();
        private readonly List<PluginUpmDependency> _mergedUpmDependencies = new List<PluginUpmDependency>();

        /// <summary>本次要装的插件，依赖在前。</summary>
        internal IReadOnlyList<PluginManifest> ToInstall => _toInstall;

        /// <summary>判定出的全部冲突。</summary>
        internal IReadOnlyList<PlanConflict> Conflicts => _conflicts;

        /// <summary>合并后的外部 UPM 依赖，同一键只留版本最高的一条。</summary>
        internal IReadOnlyList<PluginUpmDependency> MergedUpmDependencies => _mergedUpmDependencies;

        /// <summary>是否存在任一冲突。</summary>
        internal bool HasConflicts => _conflicts.Count > 0;

        internal void AddInstall(PluginManifest manifest) => _toInstall.Add(manifest);

        internal void AddConflict(PlanConflict conflict) => _conflicts.Add(conflict);

        internal void AddMergedUpmDependency(PluginUpmDependency dependency) => _mergedUpmDependencies.Add(dependency);

        internal void ReplaceMergedUpmDependency(int index, PluginUpmDependency dependency) =>
            _mergedUpmDependencies[index] = dependency;

        /// <summary>整批中止：保留冲突供展示，丢弃可执行部分。</summary>
        internal void AbortBatch()
        {
            _toInstall.Clear();
            _mergedUpmDependencies.Clear();
        }
    }
}
