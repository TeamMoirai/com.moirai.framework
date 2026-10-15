using System;
using System.IO;
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
            Assert.AreEqual(GitProcessUtility.NotRunExitCode, result.ExitCode);
            Assert.IsNotEmpty(result.FailureReason());
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

        private void BuildSourceRepo()
        {
            Directory.CreateDirectory(_sourceRepo);
            foreach (string id in PluginIds)
            {
                string folder = Path.Combine(_sourceRepo, id);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, id + PluginManifest.ManifestSuffix), "{\"id\":\"" + id + "\"}");
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
