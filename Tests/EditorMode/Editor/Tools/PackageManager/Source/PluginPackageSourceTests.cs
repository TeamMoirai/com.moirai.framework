using System;
using System.IO;
using System.Collections.Generic;
using Moirai.Atropos.Editor;
using NUnit.Framework;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 部分克隆、cone 稀疏检出与树读取的用例集，跑在临时 <c>file://</c> 夹具上。
    /// </summary>
    /// <remarks>
    /// 本地夹具不支持过滤协商（git 报 "filtering not recognized by server" 并回全量 blob），
    /// 因此"未勾选既不物化"按磁盘判，"部分克隆已请求"按配置判；真远端的少下载只能端到端验。
    /// </remarks>
    internal sealed class PluginPackageSourceTests
    {
        private const string Branch = "plugin";
        private static readonly string[] PluginIds = { "blackboard", "skill-tree", "ledger" };

        private string _root;
        private string _sourceRepo;
        private string _workspace;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "moirai-pm-t6-" + DateTime.UtcNow.Ticks);
            _sourceRepo = Path.Combine(_root, "src");
            _workspace = Path.Combine(_root, "work");
            BuildSourceRepo();
        }

        [TearDown]
        public void TearDown()
        {
            ForceDelete(_root);
        }

        [Test]
        public void EnsureClone_MissingDirectory_CreatesDetachedWorktreeWithoutMaterializingFolders()
        {
            PluginPackageSource source = NewSource();

            Assert.IsTrue(source.EnsureClone(out string failureReason), failureReason);
            Assert.IsTrue(source.IsGitWorkspace);
            Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, "blackboard")), "没摆正分支前不该物化任何插件目录");
        }

        [Test]
        public void EnsureClone_RequestsBlobNoneFilterInRemoteConfig()
        {
            var source = new PluginPackageSource(_workspace, RepoUrl(), Branch, null);
            Assert.IsTrue(source.EnsureClone(out string failureReason), failureReason);

            GitRunResult config = GitProcessUtility.Run(null, _workspace,
                "config --get remote.origin.partialclonefilter", 30000);

            Assert.AreEqual("blob:none", config.StandardOutput.Trim());
        }

        [Test]
        public void EnsureClone_ExistingNonGitDirectory_ReportsWithoutInit()
        {
            Directory.CreateDirectory(_workspace);
            File.WriteAllText(Path.Combine(_workspace, "keepme.txt"), "user data");

            PluginPackageSource source = NewSource();
            Assert.IsFalse(source.EnsureClone(out string failureReason));

            StringAssert.Contains("不是 git 工作树", failureReason);
            Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, ".git")), "绝不就地 init");
            Assert.IsTrue(File.Exists(Path.Combine(_workspace, "keepme.txt")), "绝不删用户目录里的东西");
        }

        [Test]
        public void ListPaths_ListsEveryTreePathWithoutMaterializingThem()
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsTrue(source.TryListPaths(out System.Collections.Generic.List<string> paths, out string failureReason), failureReason);
            CollectionAssert.Contains(paths, "blackboard/blackboard.plugin.json");
            CollectionAssert.Contains(paths, "skill-tree/skill-tree.plugin.json");
            CollectionAssert.Contains(paths, "package.json");
            Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, "skill-tree")), "列树靠 tree 就够，勾选外的目录不因此落盘");
        }

        [Test]
        public void ReadBlob_UnselectedPath_StillReturnsTextFromTree()
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsTrue(source.TryReadBlob("skill-tree/skill-tree.plugin.json", out string text, out string failureReason), failureReason);
            StringAssert.Contains("skill-tree", text);
            Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, "skill-tree")), "取文不等于物化");
        }

        [Test]
        [TestCase("../outside")]
        [TestCase("-f")]
        [TestCase("/abs/path")]
        [TestCase("a//b")]
        [TestCase("")]
        public void ReadBlob_UnsafeTreePath_RejectsBeforeReachingGit(string treePath)
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsFalse(source.TryReadBlob(treePath, out string text, out string failureReason), treePath);
            Assert.IsNull(text);
            StringAssert.Contains("树路径", failureReason);
        }

        [Test]
        public void ApplySparsePaths_TwoIds_MaterializesOnlyThoseDirectories()
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsTrue(source.ApplySparsePaths(new[] { "blackboard", "ledger" }, out string failureReason), failureReason);
            Assert.IsTrue(Directory.Exists(Path.Combine(_workspace, "blackboard")));
            Assert.IsTrue(Directory.Exists(Path.Combine(_workspace, "ledger")));
            Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, "skill-tree")));
            Assert.IsTrue(File.Exists(Path.Combine(_workspace, "package.json")), "cone 永远带根文件");
        }

        [Test]
        public void ApplySparsePaths_EmptySet_LeavesRootFilesOnly()
        {
            PluginPackageSource source = CloneAndAttach();
            Assert.IsTrue(source.ApplySparsePaths(new[] { "blackboard" }, out _));

            Assert.IsTrue(source.ApplySparsePaths(new string[0], out string failureReason), failureReason);
            Assert.IsFalse(Directory.Exists(Path.Combine(_workspace, "blackboard")));
            Assert.IsTrue(File.Exists(Path.Combine(_workspace, "package.json")));
        }

        [Test]
        [TestCase("../escape")]
        [TestCase("UPPER")]
        [TestCase("with space")]
        public void ApplySparsePaths_InvalidId_RejectsBeforeReachingGit(string id)
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsFalse(source.ApplySparsePaths(new[] { id }, out string failureReason), id);
            StringAssert.Contains("字符集", failureReason);
        }

        [Test]
        public void EnsureBranchAttached_FreshClone_BuildsBranchAndHeadCommit()
        {
            var source = new PluginPackageSource(_workspace, RepoUrl(), Branch, null);
            Assert.IsTrue(source.EnsureClone(out string cloneReason), cloneReason);

            Assert.IsTrue(source.EnsureBranchAttached(out string attachReason), attachReason);
            Assert.IsTrue(source.TryHeadCommit(out string commit, out string headReason), headReason);
            Assert.AreEqual(40, commit.Length, commit);
            StringAssert.Contains("refs/heads/" + Branch, ReadSymbolicRef());
        }

        [Test]
        public void EnsureBranchAttached_DetachedHead_RestoresBranchSymbolicRef()
        {
            PluginPackageSource source = CloneAndAttach();
            Assert.IsTrue(source.TryHeadCommit(out string commit, out _));
            GitProcessUtility.Run(null, _workspace, "checkout --detach " + commit, 60000);
            Assert.AreEqual("", ReadSymbolicRef(), "夹具自己要先真是分离头指针");

            Assert.IsTrue(source.EnsureBranchAttached(out string attachReason), attachReason);
            StringAssert.Contains("refs/heads/" + Branch, ReadSymbolicRef());
        }

        [Test]
        public void EnsureBranchAttached_BranchMissingOnOrigin_ReportsReason()
        {
            Assert.IsTrue(NewSource().EnsureClone(out string cloneReason), cloneReason);
            var ghost = new PluginPackageSource(_workspace, RepoUrl(), "ghost", null);

            Assert.IsFalse(ghost.EnsureBranchAttached(out string attachReason));
            StringAssert.Contains("ghost", attachReason);
        }

        [Test]
        public void TreeHashOf_WhenPathAbsentAtRevision_FailsWithoutThrowing()
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsFalse(source.TryTreeHash("nope/nope.plugin.json", "HEAD", out string hash, out string failureReason));
            Assert.IsNull(hash);
            StringAssert.Contains("nope/nope.plugin.json", failureReason);
        }

        [Test]
        public void TreeHashOf_ManifestPathAtHead_ReturnsHash()
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsTrue(source.TryTreeHash("blackboard/blackboard.plugin.json", "HEAD", out string hash, out string failureReason), failureReason);
            Assert.AreEqual(40, hash.Length, hash);
        }

        [Test]
        public void Run_GitNotOnPath_ReportsFailureWithReason()
        {
            GitRunResult result = GitProcessUtility.Run("moirai-git-not-on-path", _sourceRepo, "--version", 5000);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(GitProcessUtility.NOT_RUN_EXIT_CODE, result.ExitCode);
            Assert.IsNotEmpty(result.FailureReason());
        }

        [Test]
        public void Quote_DoublesTrailingBackslashSoTheClosingQuoteSurvives()
        {
            Assert.AreEqual("\"\"", GitProcessUtility.Quote(string.Empty));
            Assert.AreEqual("\"plain\"", GitProcessUtility.Quote("plain"));
            Assert.AreEqual("\"a b\"", GitProcessUtility.Quote("a b"));
            Assert.AreEqual("\"a\\b\"", GitProcessUtility.Quote("a\\b"));
            Assert.AreEqual("\"a\\\\\"", GitProcessUtility.Quote("a\\"), "收尾反斜杠会吃掉结束引号，必须翻倍");
            Assert.AreEqual("\"a\\\"b\"", GitProcessUtility.Quote("a\"b"));
        }

        [TestCase("bad branch")]
        [TestCase("-looked-like-option")]
        [TestCase("../escape")]
        [TestCase("trailing.")]
        public void EnsureBranchAttached_MalformedBranchName_RejectsBeforeReachingGit(string branch)
        {
            Assert.IsTrue(NewSource().EnsureClone(out string cloneReason), cloneReason);
            var source = new PluginPackageSource(_workspace, RepoUrl(), branch, null);

            Assert.IsFalse(source.EnsureBranchAttached(out string attachReason), branch);
            StringAssert.Contains("不合约定", attachReason);

            Assert.IsFalse(source.Fetch(out string fetchReason), branch);
            StringAssert.Contains("不合约定", fetchReason);
        }

        [Test]
        public void EnsureClone_MalformedRepoUrl_RejectsBeforeReachingGit()
        {
            var source = new PluginPackageSource(_workspace, "-u:ext::sh -c whoami", Branch, null);

            Assert.IsFalse(source.EnsureClone(out string failureReason));
            StringAssert.Contains("源地址形状", failureReason);
            Assert.IsFalse(Directory.Exists(_workspace), "形状不过就不该开始建检出");
        }

        [Test]
        public void TryListManifestHistory_SameVersionInTwoRevisions_KeepsTheNewerOne()
        {
            // 中间夹一次 0.1.1：第二次 0.1.0 必须真的改动清单，否则 git log -- 路径 根本不列那次提交
            CommitManifest("blackboard", "0.1.0");
            string oldest = HeadOfSource();
            CommitManifest("blackboard", "0.1.1");
            CommitManifest("blackboard", "0.1.0");
            string newest = HeadOfSource();

            PluginPackageSource source = CloneAndAttach();

            Assert.IsTrue(source.TryListManifestHistory("blackboard", out var history, out string failureReason), failureReason);
            Assert.AreEqual(2, history.Count, "同一版本只留一条");
            Assert.AreEqual("0.1.0", history[0].Version);
            Assert.IsTrue(history[0].IsHead);
            Assert.AreEqual(newest, history[0].Revision, "0.1.0 要取最新那次，不是最早那次");
            Assert.AreNotEqual(oldest, history[0].Revision);
            Assert.AreEqual("0.1.1", history[1].Version);
            Assert.IsFalse(history[1].IsHead);
        }

        [Test]
        public void TryReadBlobAt_OldRevision_ReturnsThatRevisionsText()
        {
            CommitManifest("blackboard", "0.1.0");
            string old = HeadOfSource();
            CommitManifest("blackboard", "0.2.0");

            PluginPackageSource source = CloneAndAttach();
            string treePath = "blackboard/blackboard" + PluginManifest.MANIFEST_SUFFIX;

            Assert.IsTrue(source.TryReadBlobAt(treePath, old, out string oldText, out string oldReason), oldReason);
            StringAssert.Contains("\"0.1.0\"", oldText);

            Assert.IsTrue(source.TryReadBlob(treePath, out string headText, out string headReason), headReason);
            StringAssert.Contains("\"0.2.0\"", headText);
        }

        [Test]
        public void TryMaterializeAt_OldRevision_BringsBackFilesHeadNoLongerHas()
        {
            CommitManifest("blackboard", "0.1.0");
            File.WriteAllText(Path.Combine(_sourceRepo, "blackboard", "OnlyInOld.cs"), "// old\n");
            RunInSource("add -A");
            RunInSource("commit -qm only-in-old");
            string old = HeadOfSource();

            CommitManifest("blackboard", "0.2.0");
            File.Delete(Path.Combine(_sourceRepo, "blackboard", "OnlyInOld.cs"));
            RunInSource("add -A");
            RunInSource("commit -qm drop-only-in-old");

            PluginPackageSource source = CloneAndAttach();
            Assert.IsTrue(source.ApplySparsePaths(new[] { "blackboard" }, out string sparseReason), sparseReason);

            Assert.IsTrue(source.TryMaterializeAt(old, new[] { "blackboard" }, out string materializeReason), materializeReason);

            string manifestText = File.ReadAllText(Path.Combine(_workspace, "blackboard", "blackboard" + PluginManifest.MANIFEST_SUFFIX));
            StringAssert.Contains("\"0.1.0\"", manifestText);
            Assert.IsTrue(File.Exists(Path.Combine(_workspace, "blackboard", "OnlyInOld.cs")), "那次修订里有的文件要回来");
        }

        [Test]
        public void TryListManifestHistory_InvalidId_ReportsWithoutTouchingGit()
        {
            PluginPackageSource source = CloneAndAttach();

            Assert.IsFalse(source.TryListManifestHistory("../escape", out var history, out string failureReason));
            Assert.IsNull(history);
            StringAssert.Contains("不符合字符集", failureReason);
        }

        [Test]
        public void EnsureBranchAttached_UnpushedLocalCommit_RefusesInsteadOfRewinding()
        {
            PluginPackageSource source = CloneAndAttach();
            Assert.IsTrue(source.ApplySparsePaths(new[] { "blackboard" }, out string sparseReason), sparseReason);

            string authored = Path.Combine(_workspace, "blackboard", "AuthorWork.cs");
            File.WriteAllText(authored, "// 插件作者在本地的提交内容\n");
            RunInWorkspace("add -A");
            RunInWorkspace("-c user.email=author@test -c user.name=author commit -qm \"author local commit\"");

            Assert.AreEqual(1, CountAheadOfOrigin(), "夹具自己要先真有一条未推送提交");
            GitRunResult status = GitProcessUtility.Run(null, _workspace, "status --porcelain", 60000);
            AssertIsEmpty(status.StandardOutput, "工作树必须干净，否则先被脏树门禁兜住，测不到这条");

            Assert.IsFalse(source.EnsureBranchAttached(out string attachReason), "未推送的提交不许被 checkout -B 挪走");
            StringAssert.Contains("未推送", attachReason);
            Assert.AreEqual(1, CountAheadOfOrigin(), "分支不许被重置回 origin");
            Assert.IsTrue(File.Exists(authored), "作者写的那份文件得还在");
        }

        private static void AssertIsEmpty(string text, string message)
        {
            Assert.AreEqual(string.Empty, text.Trim(), message);
        }

        private PluginPackageSource NewSource()
        {
            return new PluginPackageSource(_workspace, RepoUrl(), Branch, null);
        }

        private PluginPackageSource CloneAndAttach()
        {
            PluginPackageSource source = NewSource();
            Assert.IsTrue(source.EnsureClone(out string cloneReason), cloneReason);
            Assert.IsTrue(source.EnsureBranchAttached(out string attachReason), attachReason);
            return source;
        }

        private string RepoUrl()
        {
            return "file:///" + _sourceRepo.Replace('\\', '/');
        }

        private string ReadSymbolicRef()
        {
            GitRunResult result = GitProcessUtility.Run(null, _workspace, "symbolic-ref -q HEAD", 30000);
            return result.Ok ? result.StandardOutput.Trim() : string.Empty;
        }

        private void RunInWorkspace(string arguments)
        {
            GitRunResult result = GitProcessUtility.Run(null, _workspace, arguments, 60000);
            Assert.IsTrue(result.Ok, $"{arguments} 失败：{result.FailureReason()}");
        }

        private int CountAheadOfOrigin()
        {
            GitRunResult result = GitProcessUtility.Run(null, _workspace,
                "rev-list --count refs/remotes/origin/" + Branch + "..HEAD", 60000);
            Assert.IsTrue(result.Ok, result.FailureReason());
            int count;
            Assert.IsTrue(int.TryParse(result.StandardOutput.Trim(), out count), result.StandardOutput);
            return count;
        }

        private void BuildSourceRepo()
        {
            Directory.CreateDirectory(_sourceRepo);
            foreach (string id in PluginIds)
            {
                string folder = Path.Combine(_sourceRepo, id);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, id + PluginManifest.MANIFEST_SUFFIX), "{\"id\":\"" + id + "\"}");
                File.WriteAllText(Path.Combine(folder, "Code.cs"), "// " + id + "\n");
            }

            File.WriteAllText(Path.Combine(_sourceRepo, "package.json"),
                "{\"name\":\"com.moirai.framework.plugin\",\"version\":\"1.0.0\"}");

            RunInSource("init -q -b " + Branch + " .");
            RunInSource("config user.email pm@test.local");
            RunInSource("config user.name pm");
            RunInSource("add -A");
            RunInSource("commit -qm fixture");
        }

        /// <summary>把某插件的清单写成可解析的样子并提交一次：版本历史用例靠它造多次改动。</summary>
        /// <remarks>顺带往 <c>Code.cs</c> 追加一行——同一版本连提两次时，光写清单会"没有改动"提交失败。</remarks>
        private void CommitManifest(string id, string version)
        {
            string folder = Path.Combine(_sourceRepo, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, id + PluginManifest.MANIFEST_SUFFIX),
                "{\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"version\":\"" + version + "\"," +
                "\"minUnityVersion\":\"2022.3.0\",\"minFrameworkVersion\":\"1.0.0\"}");
            File.AppendAllText(Path.Combine(folder, "Code.cs"), "// " + version + "\n");

            RunInSource("add -A");
            RunInSource("commit -qm \"" + id + " " + version + "\"");
        }

        private string HeadOfSource()
        {
            GitRunResult result = GitProcessUtility.Run(null, _sourceRepo, "rev-parse HEAD", 30000);
            Assert.IsTrue(result.Ok, result.FailureReason());
            return result.StandardOutput.Trim();
        }

        private void RunInSource(string arguments)
        {
            GitRunResult result = GitProcessUtility.Run(null, _sourceRepo, arguments, 60000);
            Assert.IsTrue(result.Ok, $"{arguments} 失败：{result.FailureReason()}");
        }

        private static void ForceDelete(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, true);
        }
    }
}
