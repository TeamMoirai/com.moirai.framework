using System.Collections.Generic;

namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 一次反向同步的产出：成没成、写了多少、删了多少。
    /// </summary>
    /// <remarks>同步按插件逐个走，单个插件判非不阻断同批其余插件；窗口按这份报告汇总成败。</remarks>
    internal sealed class PluginSyncReport
    {
        private readonly List<string> _notes = new List<string>();

        internal PluginSyncReport(string pluginId)
        {
            PluginId = pluginId;
        }

        /// <summary>本次同步的插件标识。</summary>
        internal string PluginId { get; }

        /// <summary>本插件是否同步到底。</summary>
        internal bool Ok { get; private set; } = true;

        /// <summary>停住的那一步的原因，成功时为空。</summary>
        internal string FailureReason { get; private set; }

        /// <summary>写回的文件数。</summary>
        internal int CopiedFiles { get; private set; }

        /// <summary>为对齐源目录而删掉的目标文件数。</summary>
        internal int DeletedFiles { get; private set; }

        /// <summary>过程行。</summary>
        internal IReadOnlyList<string> Notes => _notes;

        internal void Note(string note) => _notes.Add(note);

        internal void CountCopied() => CopiedFiles++;

        internal void CountDeleted() => DeletedFiles++;

        internal void Fail(string reason)
        {
            Ok = false;
            FailureReason = reason;
        }

        /// <summary>取一行可直接展示的汇总。</summary>
        internal string Summary()
        {
            if (!Ok) return FailureReason;
            return $"写入 {CopiedFiles} 个文件，删除 {DeletedFiles} 个多出文件";
        }
    }
}
