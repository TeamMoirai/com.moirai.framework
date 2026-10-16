namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 清单历史里的一条：某个版本，以及它那次修订。
    /// </summary>
    /// <remarks>同一个版本可能在历史上出现过多次（改了别的字段再提交），这里只留最新那一次。<br />
    /// 只读值类型：用例可以直接比较两条是否同一修订。</remarks>
    internal readonly struct PluginVersionRevision
    {
        internal PluginVersionRevision(string version, string revision, bool isHead)
        {
            Version = version;
            Revision = revision;
            IsHead = isHead;
        }

        /// <summary>清单里的三段版本号。</summary>
        internal string Version { get; }

        /// <summary>该版本所在的提交号。</summary>
        internal string Revision { get; }

        /// <summary>这条是不是分支头那一次。</summary>
        internal bool IsHead { get; }
    }
}
