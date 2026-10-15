using System;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 包源在一趟读取后的现场读数，窗口顶部状态条的数据面。
    /// </summary>
    /// <remarks>
    /// 只收"盘上现在什么样"的事实，文案由窗口拼；分支不存在时 <see cref="FailureReason"/> 带原因而非抛异常。<br />
    /// 不跨域重载保存：窗口每次 OnEnable 重新取一遍。
    /// </remarks>
    internal sealed class PackageManagerSourceStatus
    {
        /// <summary>远端地址。</summary>
        internal string RepoUrl { get; private set; }

        /// <summary>配置的分支名。</summary>
        internal string Branch { get; private set; }

        /// <summary>落点目录。</summary>
        internal string PackageRootPath { get; private set; }

        /// <summary>落点目录存在。</summary>
        internal bool WorkspacePresent { get; private set; }

        /// <summary>落点是 git 工作树。</summary>
        internal bool IsGitWorkspace { get; private set; }

        /// <summary>HEAD 的提交号，取不到时为空。</summary>
        internal string HeadCommit { get; private set; }

        /// <summary>HEAD 所在分支名，分离头指针时为空。</summary>
        internal string HeadBranch { get; private set; }

        /// <summary>工作树相对索引有改动。</summary>
        internal bool IsDirty { get; private set; }

        /// <summary>账本里记的上次 fetch 时间。</summary>
        internal string LastFetchAt { get; private set; }

        /// <summary>取读数过程中的失败原因。</summary>
        internal string FailureReason { get; private set; }

        /// <summary>工作树形态的一词描述。</summary>
        internal string WorktreeShape
        {
            get
            {
                if (!IsGitWorkspace) return "不是工作树";
                if (string.IsNullOrEmpty(HeadBranch)) return "分离头指针";
                return HeadBranch + (IsDirty ? "（脏）" : string.Empty);
            }
        }

        /// <summary>源是否够格开扫：落点是工作树且没判出失败。</summary>
        internal bool IsUsable => IsGitWorkspace && string.IsNullOrEmpty(FailureReason);

        /// <summary>就地取一遍读数，不联网。</summary>
        internal static PackageManagerSourceStatus Capture(PackageManagerSettingsData settings, PluginPackageSource source)
        {
            var status = new PackageManagerSourceStatus
            {
                RepoUrl = settings?.repoUrl,
                Branch = source?.Branch,
                PackageRootPath = source?.WorkingDirectory,
                LastFetchAt = settings?.lastFetchAt
            };

            if (source == null)
            {
                status.FailureReason = "包源没建起来";
                return status;
            }

            status.WorkspacePresent = source.WorkspaceExists;
            status.IsGitWorkspace = source.IsGitWorkspace;
            if (!status.IsGitWorkspace)
            {
                status.FailureReason = status.WorkspacePresent
                    ? "落点目录已存在但不是 git 工作树"
                    : "落点还没建检出，点 Refresh 由工具建";
                return status;
            }

            source.TryHeadCommit(out string headCommit, out _);
            status.HeadCommit = headCommit;
            if (!source.TryDescribeHead(out string headBranch, out bool dirty, out string reason))
            {
                status.FailureReason = reason;
                return status;
            }

            status.HeadBranch = headBranch;
            status.IsDirty = dirty;
            return status;
        }
    }
}
