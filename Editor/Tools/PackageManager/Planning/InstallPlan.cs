using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 一次规划的结果：安装顺序与冲突。
    /// </summary>
    /// <remarks>
    /// <see cref="ToInstall"/> 已是依赖先行的顺序，编排层直接逐包落稀疏检出。<br />
    /// 任一冲突都会走 <see cref="AbortBatch"/>：冲突在场时安装清单清空，
    /// 让"整批中止"成为类型层面的事实，而不是靠调用方记得判一次。<br />
    /// 外部 UPM 依赖的"同键取最高"不在这里做：那条规则要连归属一起判，归 <c>PackageManagerStateRegistry</c> 的台账承担。
    /// </remarks>
    internal sealed class InstallPlan
    {
        private readonly List<PluginManifest> _toInstall = new List<PluginManifest>();
        private readonly List<PlanConflict> _conflicts = new List<PlanConflict>();

        /// <summary>本次要装的插件，依赖在前。</summary>
        internal IReadOnlyList<PluginManifest> ToInstall => _toInstall;

        /// <summary>判定出的全部冲突。</summary>
        internal IReadOnlyList<PlanConflict> Conflicts => _conflicts;

        /// <summary>是否存在任一冲突。</summary>
        internal bool HasConflicts => _conflicts.Count > 0;

        internal void AddInstall(PluginManifest manifest) => _toInstall.Add(manifest);

        internal void AddConflict(PlanConflict conflict) => _conflicts.Add(conflict);

        /// <summary>整批中止：保留冲突供展示，丢弃可执行部分。</summary>
        internal void AbortBatch() => _toInstall.Clear();
    }
}
