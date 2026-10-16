using System;
using System.Collections.Generic;
using System.IO;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 分支作包源的 git 侧：部分克隆、cone 稀疏检出与只读取文。
    /// </summary>
    /// <remarks>
    /// 读侧一律走 <c>HEAD</c>：fetch 之后要再摆正一次分支才看得见新提交，编排层的顺序按此排。<br />
    /// 落点已在但不是工作树时判红停手，绝不就地 <c>git init</c>，也绝不删用户目录。<br />
    /// 进 git 的目录名先过 <see cref="PluginManifest.IsValidId"/>，树路径先过 <see cref="IsSafeTreePath"/>：两处都是注入面。<br />
    /// 稀疏检出的"既不下载"只在真远端可判：本地 <c>file://</c> 夹具会报 "filtering not recognized by server" 并回全量 blob，
    /// 因此这里能判的是勾选外的目录不物化，以及部分克隆过滤器已写进配置。
    /// </remarks>
    internal sealed class PluginPackageSource
    {
        private const int CLONE_TIMEOUT_MILLISECONDS = 180000;
        private const int FETCH_TIMEOUT_MILLISECONDS = 180000;
        private const int QUERY_TIMEOUT_MILLISECONDS = 60000;

        // 列清单历史时只看最近这些次改动：一次开下拉对每个版本跑一条 git，更早的版本基本没人装
        private const int HISTORY_SCAN_LIMIT = 50;

        private readonly string _workingDirectory;
        private readonly string _repoUrl;
        private readonly string _branch;
        private readonly string _gitPath;

        internal PluginPackageSource(string workingDirectory, string repoUrl, string branch, string gitPath)
        {
            if (string.IsNullOrEmpty(workingDirectory)) throw new ArgumentException("落点目录不能为空", nameof(workingDirectory));
            if (string.IsNullOrEmpty(branch)) throw new ArgumentException("分支名不能为空", nameof(branch));

            _workingDirectory = Path.GetFullPath(workingDirectory);
            _repoUrl = repoUrl ?? string.Empty;
            _branch = branch;
            _gitPath = gitPath;
        }

        /// <summary>落点工作树目录。</summary>
        internal string WorkingDirectory => _workingDirectory;

        /// <summary>分支名。</summary>
        internal string Branch => _branch;

        /// <summary>落点目录存在。</summary>
        internal bool WorkspaceExists => Directory.Exists(_workingDirectory);

        /// <summary>落点存在且是 git 工作树。</summary>
        internal bool IsGitWorkspace => WorkspaceExists && Directory.Exists(Path.Combine(_workingDirectory, ".git"));

        /// <summary>按需建检出：部分克隆 + 不物化 + 单分支，cone 初始为空集。</summary>
        internal bool EnsureClone(out string failureReason)
        {
            if (Directory.Exists(_workingDirectory))
            {
                if (IsGitWorkspace)
                {
                    failureReason = null;
                    return true;
                }

                failureReason = $"落点目录已存在但不是 git 工作树，工具不会就地 init 也不会删除：{_workingDirectory}";
                return false;
            }

            if (string.IsNullOrEmpty(_repoUrl))
            {
                failureReason = "源地址为空，无法建检出";
                return false;
            }

            string parent = Path.GetDirectoryName(_workingDirectory);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            GitRunResult clone = GitProcessUtility.Run(_gitPath, parent,
                $"clone --filter=blob:none --no-checkout --single-branch --branch {GitProcessUtility.Quote(_branch)} " +
                $"{GitProcessUtility.Quote(_repoUrl)} {GitProcessUtility.Quote(_workingDirectory)}",
                CLONE_TIMEOUT_MILLISECONDS);
            if (!clone.Ok)
            {
                failureReason = $"建检出失败：{clone.FailureReason()}";
                return false;
            }

            GitRunResult cone = RunCone("sparse-checkout init --cone", out string coneReason);
            if (cone.Ok)
            {
                failureReason = null;
                return true;
            }

            failureReason = coneReason;
            return false;
        }

        /// <summary>摆正分支：把本地分支对齐到 origin 上的分支，并物化当前勾选集内的文件。</summary>
        internal bool EnsureBranchAttached(out string failureReason)
        {
            if (!IsGitWorkspace)
            {
                failureReason = "落点还不是 git 工作树，先建检出";
                return false;
            }

            // --no-checkout 的克隆已经建好 refs/heads/<branch> 却留空工作树：HEAD 已诞生不等于分支已摆正，
            // 因此这里一律走一次 checkout——已在目标分支且内容一致时 git 自己会跳过写入。
            GitRunResult remoteRef = RunQuery($"rev-parse --verify --quiet refs/remotes/origin/{_branch}", out _, out _);
            if (!remoteRef.Ok)
            {
                failureReason = $"origin 上没有分支 '{_branch}'";
                return false;
            }

            GitRunResult checkout = RunQuery($"checkout -B {GitProcessUtility.Quote(_branch)} refs/remotes/origin/{_branch}",
                out _, out _);
            if (!checkout.Ok)
            {
                failureReason = $"摆正分支 '{_branch}' 失败：{checkout.FailureReason()}";
                return false;
            }

            failureReason = null;
            return true;
        }

        /// <summary>取分支头，只拉 commit 与 tree。</summary>
        internal bool Fetch(out string failureReason)
        {
            GitRunResult result = RunQuery($"fetch --filter=blob:none origin {GitProcessUtility.Quote(_branch)}", out _, out _);
            failureReason = result.Ok ? null : $"fetch 失败：{result.FailureReason()}";
            return result.Ok;
        }

        /// <summary>报 HEAD 所在分支与工作树脏否；分离头指针时分支名为空。</summary>
        internal bool TryDescribeHead(out string headBranch, out bool isDirty, out string failureReason)
        {
            headBranch = null;

            GitRunResult symbolic = RunQuery("symbolic-ref --short -q HEAD", out string symbolicText, out _);
            if (symbolic.Ok) headBranch = symbolicText.Trim();
            // 分离头指针时 symbolic-ref 以非零退出，这是已知形状而不是故障。

            return TryReadDirty(out isDirty, out failureReason);
        }

        private bool TryReadDirty(out bool isDirty, out string failureReason)
        {
            GitRunResult status = RunQuery("status --porcelain", out string text, out _);
            isDirty = status.Ok && text.Length > 0;
            failureReason = status.Ok ? null : $"读工作树状态失败：{status.FailureReason()}";
            return status.Ok;
        }

        /// <summary>取当前 HEAD 的提交号。</summary>
        internal bool TryHeadCommit(out string commit, out string failureReason)
        {
            GitRunResult result = RunQuery("rev-parse HEAD", out string text, out _);
            commit = text.Trim();
            failureReason = result.Ok ? null : $"读 HEAD 失败：{result.FailureReason()}";
            return result.Ok;
        }

        /// <summary>列整棵树的路径，勾选外的目录也在。</summary>
        internal bool TryListPaths(out List<string> paths, out string failureReason)
        {
            GitRunResult result = RunQuery("ls-tree -r --name-only HEAD", out string text, out _);
            paths = SplitLines(text);
            failureReason = result.Ok ? null : $"列树失败：{result.FailureReason()}";
            return result.Ok;
        }

        /// <summary>读一个树路径在 HEAD 上的文本内容。</summary>
        internal bool TryReadBlob(string treePath, out string text, out string failureReason)
        {
            return TryReadBlobAt(treePath, "HEAD", out text, out failureReason);
        }

        /// <summary>读某修订下一个树路径的文本内容（装历史版本得看那一次写下的清单）。</summary>
        internal bool TryReadBlobAt(string treePath, string revision, out string text, out string failureReason)
        {
            text = null;
            if (!IsSafeTreePath(treePath) || !IsSafeRevision(revision))
            {
                failureReason = $"树路径或修订不合约定：'{treePath}' @ '{revision}'";
                return false;
            }

            GitRunResult result = RunQuery($"show {GitProcessUtility.Quote(revision + ":" + treePath)}", out string body, out _);
            if (!result.Ok)
            {
                failureReason = $"读 '{treePath}' @ '{revision}' 失败：{result.FailureReason()}";
                return false;
            }

            text = body;
            failureReason = null;
            return true;
        }

        /// <summary>取某修订下某路径的 tree 或 blob 哈希，路径不在该修订时判非。</summary>
        internal bool TryTreeHash(string treePath, string revision, out string hash, out string failureReason)
        {
            hash = null;
            if (!IsSafeTreePath(treePath) || !IsSafeRevision(revision))
            {
                failureReason = $"树路径或修订不合约定：'{treePath}' @ '{revision}'";
                return false;
            }

            GitRunResult result = RunQuery($"rev-parse --verify {GitProcessUtility.Quote(revision + ":" + treePath)}", out string body, out _);
            if (!result.Ok)
            {
                failureReason = $"'{treePath}' 不在 {revision}：{result.FailureReason()}";
                return false;
            }

            hash = body.Trim();
            failureReason = null;
            return true;
        }

        /// <summary>列某插件清单历史上出现过的版本：每版本取最新那次修订，新的在前。</summary>
        /// <remarks>只看最近 <c>HISTORY_SCAN_LIMIT</c> 次改动，且同版本合并——一次开出版本下拉要对每个候选跑一条 git。</remarks>
        internal bool TryListManifestHistory(string pluginId, out List<PluginVersionRevision> history, out string failureReason)
        {
            history = null;
            if (!PluginManifest.IsValidId(pluginId))
            {
                failureReason = $"插件标识 '{pluginId}' 不符合字符集 [a-z0-9._-]，不交给 git";
                return false;
            }

            string treePath = pluginId + "/" + pluginId + PluginManifest.MANIFEST_SUFFIX;
            GitRunResult result = RunQuery(
                $"log --format=%H -n {HISTORY_SCAN_LIMIT} -- {GitProcessUtility.Quote(treePath)}",
                out string text, out _);
            if (!result.Ok)
            {
                failureReason = $"列 '{treePath}' 的历史失败：{result.FailureReason()}";
                return false;
            }

            List<string> revisions = SplitLines(text);
            var seenVersions = new HashSet<string>(StringComparer.Ordinal);
            var found = new List<PluginVersionRevision>();

            for (int i = 0; i < revisions.Count; i++)
            {
                if (!TryReadBlobAt(treePath, revisions[i], out string json, out _)) continue;
                if (!PluginManifest.TryParse(treePath, json, out PluginManifest manifest, out _)) continue;
                if (!seenVersions.Add(manifest.version)) continue;

                found.Add(new PluginVersionRevision(manifest.version, revisions[i], i == 0));
            }

            history = found;
            failureReason = null;
            return true;
        }

        /// <summary>把这些插件目录按指定修订物化进工作树：装历史版本用。</summary>
        /// <remarks>走 <c>restore --worktree</c> 而不是 <c>checkout</c>——后者会把内容写进索引，落点工作树的 git status 就脏了。<br />
        /// 路径一律取自 git 自己列出的清单，再过一遍 <see cref="IsSafeTreePath"/> 才拼进命令行。</remarks>
        internal bool TryMaterializeAt(string revision, IReadOnlyList<string> ids, out string failureReason)
        {
            if (!IsSafeRevision(revision))
            {
                failureReason = $"修订不合约定：'{revision}'";
                return false;
            }

            var paths = new List<string>();
            for (int i = 0; ids != null && i < ids.Count; i++)
            {
                string id = ids[i];
                if (!PluginManifest.IsValidId(id))
                {
                    failureReason = $"目录名 '{id}' 不符合字符集 [a-z0-9._-]，不交给 git";
                    return false;
                }

                GitRunResult listed = RunQuery(
                    $"ls-tree -r --name-only {GitProcessUtility.Quote(revision)} -- {GitProcessUtility.Quote(id + "/")}",
                    out string text, out _);
                if (!listed.Ok)
                {
                    failureReason = $"{id} 在 {revision} 里列不出文件：{listed.FailureReason()}";
                    return false;
                }

                paths.AddRange(SplitLines(text));
            }

            if (paths.Count == 0)
            {
                failureReason = null;
                return true;
            }

            var quoted = new List<string>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
            {
                if (!IsSafeTreePath(paths[i]))
                {
                    failureReason = $"修订里的路径不合约定：'{paths[i]}'";
                    return false;
                }

                quoted.Add(GitProcessUtility.Quote(paths[i]));
            }

            GitRunResult restored = RunCone(
                $"restore --source={GitProcessUtility.Quote(revision)} --worktree -- " + string.Join(" ", quoted),
                out failureReason);
            return restored.Ok;
        }

        /// <summary>把 cone 勾选集换成这批目录名，只物化它们与根文件。</summary>
        internal bool ApplySparsePaths(IReadOnlyList<string> ids, out string failureReason)
        {
            if (ids == null)
            {
                failureReason = "勾选集为空引用";
                return false;
            }

            var quoted = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (!PluginManifest.IsValidId(id))
                {
                    failureReason = $"目录名 '{id}' 不符合字符集 [a-z0-9._-]，不交给 git";
                    return false;
                }

                quoted.Add(GitProcessUtility.Quote(id));
            }

            string arguments = quoted.Count == 0
                ? "sparse-checkout set --cone"
                : "sparse-checkout set --cone " + string.Join(" ", quoted);

            GitRunResult result = RunQuery(arguments, out _, out _);
            failureReason = result.Ok ? null : $"设置勾选集失败：{result.FailureReason()}";
            return result.Ok;
        }

        /// <summary>修订可否交给 git：拒绝空、选项形状、空白与 <c>: ~ ^</c>（会与 <c>&lt;rev&gt;:&lt;path&gt;</c> 或修订运算符混义）。</summary>
        private static bool IsSafeRevision(string revision)
        {
            if (string.IsNullOrEmpty(revision) || revision[0] == '-') return false;

            for (int i = 0; i < revision.Length; i++)
            {
                char c = revision[i];
                if (char.IsWhiteSpace(c) || c == ':' || c == '~' || c == '^') return false;
            }

            return true;
        }

        /// <summary>树路径可否交给 git：拒绝选项形状、绝对形状与上跳段。</summary>
        private static bool IsSafeTreePath(string treePath)
        {
            if (string.IsNullOrEmpty(treePath)) return false;
            if (treePath[0] == '-' || treePath[0] == '/' || treePath[0] == '\\') return false;
            if (treePath.IndexOf('\\') >= 0) return false;

            string[] segments = treePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == ".." || segments[i].Length == 0) return false;
            }

            return true;
        }

        private static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            string[] parts = text.Replace('\r', '\n').Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0) lines.Add(parts[i]);
            }

            return lines;
        }

        private GitRunResult RunQuery(string arguments, out string standardOutput, out string failureReason)
        {
            GitRunResult result = GitProcessUtility.Run(_gitPath, _workingDirectory, arguments, QUERY_TIMEOUT_MILLISECONDS);
            standardOutput = result.StandardOutput;
            failureReason = result.Ok ? null : result.FailureReason();
            return result;
        }

        private GitRunResult RunCone(string arguments, out string failureReason)
        {
            GitRunResult result = GitProcessUtility.Run(_gitPath, _workingDirectory, arguments, QUERY_TIMEOUT_MILLISECONDS);
            failureReason = result.Ok ? null : $"{arguments} 失败：{result.FailureReason()}";
            return result;
        }
    }
}
