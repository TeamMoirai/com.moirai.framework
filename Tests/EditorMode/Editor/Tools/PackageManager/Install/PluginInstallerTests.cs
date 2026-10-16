using System;
using System.IO;
using Moirai.Atropos.Editor;
using NUnit.Framework;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 编排层的用例集：引擎版本号取段，以及扫分支在远端缺席与清单坏掉时的行为。
    /// </summary>
    internal sealed class PluginInstallerTests
    {
        private const string PLUGIN_ID = "blackboard";
        private const string PLUGIN_FOLDER = "com.moirai.framework.plugin";

        private const string GOOD_MANIFEST =
            "{\"id\":\"blackboard\",\"name\":\"Blackboard\",\"function\":\"Data\",\"description\":\"d\"," +
            "\"author\":\"a\",\"version\":\"0.1.0\",\"minUnityVersion\":\"2022.3.0\",\"minFrameworkVersion\":\"1.0.0\"," +
            "\"tags\":[],\"dependencies\":[],\"upmDependencies\":[]}";
        [Test]
        [TestCase("2022.3.62f3", true)]
        [TestCase("6000.3.26f1", true)]
        [TestCase("2021.3.0", true)]
        [TestCase("6000.1", false)]
        [TestCase("2022.3.a1f1", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void TryParseEngineVersion_EngineShapes(string text, bool expectOk)
        {
            Assert.AreEqual(expectOk, PluginInstaller.TryParseEngineVersion(text, out _), text ?? "<null>");
        }

        [Test]
        public void TryParseEngineVersion_TrailingReleaseSuffix_KeepsFirstThreeSegments()
        {
            Assert.IsTrue(PluginInstaller.TryParseEngineVersion("6000.3.26f1", out PluginVersion version));
            Assert.AreEqual(6000, version.Major);
            Assert.AreEqual(3, version.Minor);
            Assert.AreEqual(26, version.Patch);
        }

        [Test]
        public void TryParseEngineVersion_FourSegmentsWithBuildNumber_KeepsFirstThree()
        {
            Assert.IsTrue(PluginInstaller.TryParseEngineVersion("2022.3.62.1234", out PluginVersion version));
            Assert.AreEqual("2022.3.62", version.ToString());
        }

        [Test]
        public void TryBrowse_RemoteUnreachable_BuildsCatalogFromLocalBranch()
        {
            string root = Path.Combine(Path.GetTempPath(), "pm-installer-" + Guid.NewGuid().ToString("N"));
            string sourceRepo = Path.Combine(root, "src");
            string workspace = Path.Combine(root, PLUGIN_FOLDER);

            try
            {
                BuildSourceRepo(sourceRepo, GOOD_MANIFEST);
                PluginInstaller installer = NewInstaller(sourceRepo, workspace);

                var online = new PluginInstallReport();
                Assert.IsTrue(installer.TryBrowse(online, out _, out string head), online.FailureReason);
                Assert.IsNotEmpty(head);
                Assert.IsTrue(installer.LastFetchReachedRemote, "夹具自己要先真是能到远端的样子");

                ForceDelete(sourceRepo);

                var offline = new PluginInstallReport();
                Assert.IsTrue(installer.TryBrowse(offline, out PluginCatalogResult catalog, out _), offline.FailureReason);
                Assert.IsFalse(installer.LastFetchReachedRemote, "没到远端却记成到了，窗口就会写假的上次刷新时刻");
                Assert.AreEqual(1, catalog.Manifests.Count, string.Join(" | ", catalog.Errors));
                Assert.AreEqual(PLUGIN_ID, catalog.Manifests[0].id);
                StringAssert.Contains("离线", string.Join(" | ", offline.Notes));
            }
            finally
            {
                ForceDelete(root);
            }
        }

        [Test]
        public void TryBrowse_BrokenManifest_ReportsPerFileAndKeepsBrowseWorking()
        {
            string root = Path.Combine(Path.GetTempPath(), "pm-installer-" + Guid.NewGuid().ToString("N"));
            string sourceRepo = Path.Combine(root, "src");
            string workspace = Path.Combine(root, PLUGIN_FOLDER);

            try
            {
                BuildSourceRepo(sourceRepo, GOOD_MANIFEST);
                string brokenFolder = Path.Combine(sourceRepo, "broken");
                Directory.CreateDirectory(brokenFolder);
                File.WriteAllText(Path.Combine(brokenFolder, "broken" + PluginManifest.MANIFEST_SUFFIX), "{坏 JSON");
                RunGit(sourceRepo, "add -A");
                RunGit(sourceRepo, "commit -qm broken");

                PluginInstaller installer = NewInstaller(sourceRepo, workspace);
                var report = new PluginInstallReport();
                Assert.IsTrue(installer.TryBrowse(report, out PluginCatalogResult catalog, out _), report.FailureReason);

                Assert.AreEqual(1, catalog.Manifests.Count, "坏一份不许带走好那份");
                Assert.AreEqual(1, catalog.Errors.Count);
                StringAssert.Contains("broken/broken" + PluginManifest.MANIFEST_SUFFIX, catalog.Errors[0]);
            }
            finally
            {
                ForceDelete(root);
            }
        }

        private static PluginInstaller NewInstaller(string sourceRepo, string workspace)
        {
            var settings = new PackageManagerSettingsData
            {
                repoUrl = "file:///" + sourceRepo.Replace('\\', '/'),
                branch = "plugin",
                gitPath = null
            };

            return new PluginInstaller(settings, workspace, Path.Combine(Path.GetDirectoryName(workspace), "manifest.json"));
        }

        private static void BuildSourceRepo(string sourceRepo, string manifestJson)
        {
            string folder = Path.Combine(sourceRepo, PLUGIN_ID);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(sourceRepo, "package.json"),
                "{\"name\":\"" + PLUGIN_FOLDER + "\",\"version\":\"1.0.0\"}");
            File.WriteAllText(Path.Combine(folder, PLUGIN_ID + PluginManifest.MANIFEST_SUFFIX), manifestJson);

            RunGit(sourceRepo, "init -q -b plugin .");
            RunGit(sourceRepo, "config user.email pm@test.local");
            RunGit(sourceRepo, "config user.name pm");
            RunGit(sourceRepo, "add -A");
            RunGit(sourceRepo, "commit -qm fixture");
        }

        private static void RunGit(string workingDirectory, string arguments)
        {
            GitRunResult result = GitProcessUtility.Run(null, workingDirectory, arguments, 60000);
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
