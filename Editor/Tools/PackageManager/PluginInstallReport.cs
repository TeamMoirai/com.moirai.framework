using System.Collections.Generic;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 一次装卸编排的产出：成没成、走过哪些步、哪些冲突与谁还依赖它。
    /// </summary>
    /// <remarks>
    /// 编排层逐步记账，任一步判非即停在那里并把原因交给窗口；<see cref="Ok"/> 为假时不写盘也不刷新。<br />
    /// <see cref="Dependents"/> 只在场依赖方时非空，窗口据此决定是否弹窗。
    /// </remarks>
    internal sealed class PluginInstallReport
    {
        private readonly List<string> _notes = new List<string>();
        private readonly List<PlanConflict> _conflicts = new List<PlanConflict>();
        private readonly List<string> _touchedPluginIds = new List<string>();
        private readonly List<string> _dependents = new List<string>();

        /// <summary>本次编排是否走到底。</summary>
        internal bool Ok { get; private set; } = true;

        /// <summary>停住的那一步的原因，成功时为空。</summary>
        internal string FailureReason { get; private set; }

        /// <summary>逐步记账的过程行。</summary>
        internal IReadOnlyList<string> Notes => _notes;

        /// <summary>规划阶段判出的冲突。</summary>
        internal IReadOnlyList<PlanConflict> Conflicts => _conflicts;

        /// <summary>本次实际写入或回收的插件 id。</summary>
        internal IReadOnlyList<string> TouchedPluginIds => _touchedPluginIds;

        /// <summary>仍在场的依赖方。</summary>
        internal IReadOnlyList<string> Dependents => _dependents;

        /// <summary>是否存在任一冲突。</summary>
        internal bool HasConflicts => _conflicts.Count > 0;

        internal void Note(string note) => _notes.Add(note);

        internal void AddConflict(PlanConflict conflict) => _conflicts.Add(conflict);

        internal void AddTouched(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId) || _touchedPluginIds.Contains(pluginId)) return;
            _touchedPluginIds.Add(pluginId);
        }

        internal void AddDependent(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId) || _dependents.Contains(pluginId)) return;
            _dependents.Add(pluginId);
        }

        /// <summary>记失败原因并保留已走过的步骤。</summary>
        internal void Fail(string reason)
        {
            Ok = false;
            FailureReason = reason;
        }

        /// <summary>取一行可直接展示的汇总。</summary>
        internal string Summary()
        {
            if (!Ok) return FailureReason;
            if (_touchedPluginIds.Count == 0) return "没有需要改动的插件";
            return "处理了 " + _touchedPluginIds.Count + " 个插件：" + string.Join("、", _touchedPluginIds);
        }
    }
}
