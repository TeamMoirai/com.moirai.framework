using System;
using System.IO;
using Moirai.Atropos.PackageManager;
using NUnit.Framework;

namespace Tools.PackageManager
{
    /// <summary>
    /// 已装插件目录反向同步回本地 git 工作树的用例集，跑在临时目录夹具上。
    /// </summary>
    internal sealed class PluginFolderSyncTests
    {
        private const string PluginId = "blackboard";

        private string _root;
        private string _source;
        private string _targetRoot;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "pm-sync-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "Packages", "com.moirai.framework.plugin." + PluginId);
            _targetRoot = Path.Combine(_root, "local-repo");

            Directory.CreateDirectory(Path.Combine(_source, "Editor"));
            Directory.CreateDirectory(_targetRoot);

            Write(Path.Combine(_source, PluginId + PluginManifest.MANIFEST_SUFFIX), "{\"id\":\"" + PluginId + "\"}");
            Write(Path.Combine(_source, PluginId + PluginManifest.MANIFEST_SUFFIX + ".meta"), "meta");
            Write(Path.Combine(_source, "Code.cs"), "// v2\n");
            Write(Path.Combine(_source, "Editor", "Tool.cs"), "// tool\n");

            // 安装时生成的嵌入包清单：不是分支内容，反向同步不写回
            Write(Path.Combine(_source, "package.json"), "{\"name\":\"generated\"}");
            Write(Path.Combine(_source, "package.json.meta"), "meta");
        }

        [TearDown]
        public void TearDown()
        {
            ForceDelete(_root);
        }

        [Test]
        public void Sync_NewPlugin_CopiesNestedFilesAndMeta()
        {
            PluginSyncReport report = PluginFolderSync.Sync(PluginId, _source, _targetRoot);

            Assert.IsTrue(report.Ok, report.FailureReason);
            Assert.AreEqual(4, report.CopiedFiles, report.Summary());

            string target = Path.Combine(_targetRoot, PluginId);
            Assert.AreEqual("// v2\n", File.ReadAllText(Path.Combine(target, "Code.cs")));
            Assert.AreEqual("// tool\n", File.ReadAllText(Path.Combine(target, "Editor", "Tool.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(target, PluginId + PluginManifest.MANIFEST_SUFFIX + ".meta")),
                "分支上跟踪的 .meta 要照常写回");
            Assert.IsFalse(File.Exists(Path.Combine(target, "package.json")), "安装生成的根 package.json 不该写回");
            Assert.IsFalse(File.Exists(Path.Combine(target, "package.json.meta")), "它的 .meta 也不该写回");
        }

        [Test]
        public void Sync_MirrorMode_OverwritesModifiedAndDeletesExtraFiles()
        {
            string target = Path.Combine(_targetRoot, PluginId);
            Directory.CreateDirectory(target);
            Write(Path.Combine(target, "Code.cs"), "// v1\n");
            Write(Path.Combine(target, "Old.cs"), "// stale\n");
            Write(Path.Combine(target, "package.json"), "{\"keep\":true}");

            PluginSyncReport report = PluginFolderSync.Sync(PluginId, _source, _targetRoot);

            Assert.IsTrue(report.Ok, report.FailureReason);
            Assert.AreEqual(1, report.DeletedFiles, "目标里多出的源文件要删掉");
            Assert.AreEqual("// v2\n", File.ReadAllText(Path.Combine(target, "Code.cs")), "同名文件要被源覆盖");
            Assert.IsFalse(File.Exists(Path.Combine(target, "Old.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "package.json")), "安装产物不参与镜像，不该被删");
        }

        [Test]
        public void Sync_InvalidPluginId_RejectsBeforeTouchingDisk()
        {
            PluginSyncReport report = PluginFolderSync.Sync("../escape", _source, _targetRoot);

            Assert.IsFalse(report.Ok);
            StringAssert.Contains("字符集", report.FailureReason);
            Assert.IsEmpty(Directory.GetDirectories(_targetRoot), "形状不过就不该写任何目录");
        }

        [Test]
        public void Sync_MissingInstalledFolder_ReportsReason()
        {
            PluginSyncReport report = PluginFolderSync.Sync(PluginId, Path.Combine(_root, "nope"), _targetRoot);

            Assert.IsFalse(report.Ok);
            StringAssert.Contains("已装目录不存在", report.FailureReason);
        }

        [Test]
        public void Sync_TargetEqualsInstalledFolder_RejectsWithoutSelfCopy()
        {
            string parent = Path.Combine(_root, "equal");
            string source = Path.Combine(parent, PluginId);
            Directory.CreateDirectory(source);

            PluginSyncReport report = PluginFolderSync.Sync(PluginId, source, parent);

            Assert.IsFalse(report.Ok);
            StringAssert.Contains("重叠", report.FailureReason);
        }

        [Test]
        public void Sync_TargetInsideInstalledFolder_RejectsWithoutRecursion()
        {
            string nested = Path.Combine(_source, "nested");
            Directory.CreateDirectory(nested);

            PluginSyncReport report = PluginFolderSync.Sync(PluginId, _source, nested);

            Assert.IsFalse(report.Ok);
            StringAssert.Contains("重叠", report.FailureReason);
            Assert.IsFalse(Directory.Exists(Path.Combine(nested, PluginId)), "拒绝后不该建出同步落点");
        }

        private static void Write(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, text);
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
