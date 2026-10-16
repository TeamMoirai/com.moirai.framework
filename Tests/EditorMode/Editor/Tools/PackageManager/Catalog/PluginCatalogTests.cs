using System;
using System.Collections.Generic;
using Moirai.Atropos.Editor;
using NUnit.Framework;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 分支树到插件目录的发现用例集，取文走委托注入因此不碰 git。
    /// </summary>
    internal sealed class PluginCatalogTests
    {
        private const string BlackboardJson =
            "{\"id\":\"blackboard\",\"name\":\"Blackboard\",\"function\":\"Data\",\"description\":\"d\"," +
            "\"author\":\"a\",\"version\":\"0.1.0\",\"minUnityVersion\":\"2022.3.0\"," +
            "\"minFrameworkVersion\":\"1.1.0\"," +
            "\"tags\":[\"MOIRAI_BLACKBOARD\"],\"dependencies\":[],\"upmDependencies\":[]}";

        private const string LedgerJson =
            "{\"id\":\"ledger\",\"name\":\"Ledger\",\"function\":\"Data\",\"description\":\"d\"," +
            "\"author\":\"a\",\"version\":\"0.2.0\",\"minUnityVersion\":\"2022.3.0\"," +
            "\"minFrameworkVersion\":\"1.1.0\"," +
            "\"tags\":[],\"dependencies\":[],\"upmDependencies\":[]}";

        private static Func<string, string> BlobOf(params KeyValuePair<string, string>[] entries)
        {
            var table = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> entry in entries) table[entry.Key] = entry.Value;
            return path => table.TryGetValue(path, out string text) ? text : null;
        }

        private static KeyValuePair<string, string> At(string path, string text)
        {
            return new KeyValuePair<string, string>(path, text);
        }

        [Test]
        public void Build_SingleManifestUnderIdFolder_ParsesIt()
        {
            PluginCatalogResult result = PluginCatalog.Build(new[] { "blackboard/blackboard.plugin.json" },
                BlobOf(At("blackboard/blackboard.plugin.json", BlackboardJson)));

            Assert.IsEmpty(result.Errors, string.Join(" | ", result.Errors));
            Assert.AreEqual(1, result.Manifests.Count);
            Assert.AreEqual("blackboard", result.Manifests[0].id);
            Assert.IsTrue(result.ById.TryGetValue("blackboard", out PluginManifest byId));
            Assert.AreSame(result.Manifests[0], byId);
        }

        [Test]
        public void Build_IgnoresNonManifestJsonFiles()
        {
            PluginCatalogResult result = PluginCatalog.Build(new[]
            {
                "blackboard/package.json", "blackboard/README.md", "blackboard/blackboard.json",
                "package.json", "blackboard/blackboard.plugin.json"
            }, BlobOf(At("blackboard/blackboard.plugin.json", BlackboardJson),
                At("blackboard/package.json", "{\"name\":\"com.moirai.framework.plugin.blackboard\"}")));

            Assert.AreEqual(1, result.Manifests.Count, string.Join(" | ", result.Errors));
            Assert.IsEmpty(result.Errors);
        }

        [Test]
        public void Build_SameIdUnderTwoFolders_ReportsConflictAndKeepsNeitherInById()
        {
            PluginCatalogResult result = PluginCatalog.Build(new[]
            {
                "blackboard/blackboard.plugin.json", "nested/blackboard/blackboard.plugin.json"
            }, BlobOf(At("blackboard/blackboard.plugin.json", BlackboardJson),
                At("nested/blackboard/blackboard.plugin.json", BlackboardJson)));

            Assert.IsEmpty(result.Manifests, "撞名的两边都不算可装");
            Assert.IsFalse(result.ById.ContainsKey("blackboard"));
            Assert.AreEqual(1, result.Errors.Count, string.Join(" | ", result.Errors));
            StringAssert.Contains("blackboard", result.Errors[0]);
        }

        [Test]
        public void Build_FolderNameMismatchingManifestName_ReportsPerFileError()
        {
            PluginCatalogResult result = PluginCatalog.Build(new[] { "board/blackboard.plugin.json" },
                BlobOf(At("board/blackboard.plugin.json", BlackboardJson)));

            Assert.IsEmpty(result.Manifests);
            Assert.AreEqual(1, result.Errors.Count, string.Join(" | ", result.Errors));
            StringAssert.Contains("board/blackboard.plugin.json", result.Errors[0]);
        }

        [Test]
        public void Build_BrokenManifest_ReportsPerFileAndContinuesScanning()
        {
            PluginCatalogResult result = PluginCatalog.Build(new[]
            {
                "ledger/ledger.plugin.json", "blackboard/blackboard.plugin.json"
            }, BlobOf(At("ledger/ledger.plugin.json", LedgerJson), At("blackboard/blackboard.plugin.json", "{坏 JSON")));

            Assert.AreEqual(1, result.Manifests.Count, "坏一格不清盘");
            Assert.AreEqual("ledger", result.Manifests[0].id);
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("blackboard/blackboard.plugin.json", result.Errors[0]);
        }

        [Test]
        public void Build_BlobUnavailable_ReportsPerFileAndContinuesScanning()
        {
            PluginCatalogResult result = PluginCatalog.Build(new[]
            {
                "ledger/ledger.plugin.json", "blackboard/blackboard.plugin.json"
            }, BlobOf(At("ledger/ledger.plugin.json", LedgerJson)));

            Assert.AreEqual(1, result.Manifests.Count);
            Assert.AreEqual(1, result.Errors.Count, string.Join(" | ", result.Errors));
            StringAssert.Contains("blackboard/blackboard.plugin.json", result.Errors[0]);
        }

        [Test]
        public void Build_IdMismatchingItsOwnManifest_ReportsValidationReason()
        {
            string wrongIdJson = BlackboardJson.Replace("\"blackboard\"", "\"board\"");
            PluginCatalogResult result = PluginCatalog.Build(new[] { "blackboard/blackboard.plugin.json" },
                BlobOf(At("blackboard/blackboard.plugin.json", wrongIdJson)));

            Assert.IsEmpty(result.Manifests);
            Assert.AreEqual(1, result.Errors.Count, string.Join(" | ", result.Errors));
        }

        [Test]
        public void Build_EmptyTree_ReturnsEmptyWithoutError()
        {
            PluginCatalogResult result = PluginCatalog.Build(new string[0], path => "{}");

            Assert.IsEmpty(result.Manifests);
            Assert.IsEmpty(result.Errors);
            Assert.IsEmpty(result.ById);
        }
    }
}
