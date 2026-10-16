using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Moirai.Atropos.Editor;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 已装状态与 UPM 依赖归属账本的用例集。
    /// </summary>
    internal sealed class PackageManagerStateRegistryTests
    {
        private const string ManifestWithUserEntry = @"{
  ""dependencies"": {
    ""com.unity.cinemachine"": ""3.1.7"",
    ""com.cysharp.unitask"": ""2.5.11""
  }
}";

        private const string ManifestClean = @"{
  ""dependencies"": {
    ""com.unity.cinemachine"": ""3.1.7""
  }
}";

        private string _statePath;

        [SetUp]
        public void SetUp()
        {
            _statePath = Path.Combine(Path.GetTempPath(), $"pm-state-{Guid.NewGuid():N}.json");
            PackageManagerStateRegistry.Internal_UseStatePath(_statePath);

            Assert.IsFalse(File.Exists(_statePath), "夹具起点必须没有状态文件");
            Assert.IsNull(PackageManagerStateRegistry.FindInstalled(PackageManagerStateRegistry.Load(), "blackboard"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (File.Exists(_statePath)) File.Delete(_statePath);
                foreach (string leftover in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_statePath) + ".corrupt-*"))
                {
                    File.Delete(leftover);
                }
            }
            finally
            {
                PackageManagerStateRegistry.Internal_ResetStatePath();
            }
        }

        private static PackageManagerStateData NewState() => PackageManagerStateRegistry.Load();

        private static PluginUpmDependency Upm(string id, string version)
        {
            return new PluginUpmDependency { id = id, version = version };
        }

        [Test]
        public void Load_MissingFile_ReturnsDefaultBranch()
        {
            PackageManagerStateData state = NewState();
            Assert.IsNotNull(state.settings);
            Assert.AreEqual("plugin", state.settings.branch);
            Assert.AreEqual("git", state.settings.gitPath);
            Assert.AreEqual("https://github.com/TeamMoirai/com.moirai.framework.git", state.settings.repoUrl);
        }

        [Test]
        public void StampFetch_RecordsParseableTime()
        {
            PackageManagerStateData state = NewState();
            Assert.IsTrue(string.IsNullOrEmpty(state.settings.lastFetchAt));

            PackageManagerStateRegistry.StampFetch(state);

            Assert.IsTrue(DateTime.TryParse(state.settings.lastFetchAt, out _), state.settings.lastFetchAt);
        }

        [Test]
        public void CachedCatalog_SurvivesSaveAndLoad()
        {
            PackageManagerStateData state = NewState();
            var catalog = new PluginCatalogResult();
            catalog.AddManifest(Manifest("zlogger", "ZLOGGER_INSTALLED"));
            PackageManagerStateRegistry.CacheCatalog(state, catalog, "abc1234");
            PackageManagerStateRegistry.Save(state);

            PackageManagerStateData reloaded = PackageManagerStateRegistry.Load();

            Assert.IsTrue(PackageManagerStateRegistry.TryGetCachedCatalog(reloaded, out List<PluginManifest> manifests, out string headCommit));
            Assert.AreEqual(1, manifests.Count);
            Assert.AreEqual("zlogger", manifests[0].id);
            Assert.AreEqual(1, manifests[0].tags.Length);
            Assert.AreEqual("ZLOGGER_INSTALLED", manifests[0].tags[0]);
            Assert.AreEqual("abc1234", headCommit);
        }

        [Test]
        public void TryGetCachedCatalog_ChangedBranch_ReturnsFalse()
        {
            PackageManagerStateData state = NewState();
            var catalog = new PluginCatalogResult();
            catalog.AddManifest(Manifest("zlogger", "ZLOGGER_INSTALLED"));
            PackageManagerStateRegistry.CacheCatalog(state, catalog, "abc1234");

            state.settings.branch = "another-branch";

            Assert.IsFalse(PackageManagerStateRegistry.TryGetCachedCatalog(state, out _, out _));
        }

        [Test]
        public void TryGetCachedCatalog_NoCache_ReturnsFalse()
        {
            Assert.IsFalse(PackageManagerStateRegistry.TryGetCachedCatalog(NewState(), out _, out _));
        }

        private static PluginManifest Manifest(string id, params string[] tags)
        {
            return new PluginManifest
            {
                id = id,
                name = id,
                version = "0.1.0",
                minUnityVersion = "2022.3.0",
                minFrameworkVersion = "1.1.0",
                tags = tags
            };
        }

        [Test]
        public void UpsertInstalled_SameIdTwice_KeepsSingleEntryWithLatestValue()
        {
            PackageManagerStateData state = NewState();

            PackageManagerStateRegistry.UpsertInstalled(state, new InstalledPluginData { id = "blackboard", version = "0.1.0" });
            PackageManagerStateRegistry.UpsertInstalled(state, new InstalledPluginData { id = "blackboard", version = "0.2.0" });

            Assert.AreEqual(1, state.installed.Count, "重复安装不得长重复项");
            Assert.AreEqual("0.2.0", PackageManagerStateRegistry.FindInstalled(state, "blackboard").version);
        }

        [Test]
        public void RemoveInstalled_UnknownId_ReturnsFalse()
        {
            PackageManagerStateData state = NewState();
            Assert.IsFalse(PackageManagerStateRegistry.RemoveInstalled(state, "nope"));
        }

        [Test]
        public void RemoveInstalled_KnownId_ReturnsTrueAndDropsEntry()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.UpsertInstalled(state, new InstalledPluginData { id = "blackboard" });

            Assert.IsTrue(PackageManagerStateRegistry.RemoveInstalled(state, "blackboard"));
            Assert.IsNull(PackageManagerStateRegistry.FindInstalled(state, "blackboard"));
        }

        [Test]
        public void RecordUpmOwner_AbsentEverywhere_WritesLineAndMarksOwned()
        {
            PackageManagerStateData state = NewState();

            UpmEntryOutcome outcome = PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean,
                Upm("com.cysharp.zstring", "2.6.0"), "blackboard", out string newText);

            Assert.AreEqual(UpmEntryOutcome.Added, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(newText, "com.cysharp.zstring", out string version));
            Assert.AreEqual("2.6.0", version);
            Assert.IsFalse(state.upmOwnership[0].preexisting);
            CollectionAssert.AreEqual(new[] { "blackboard" }, state.upmOwnership[0].owners);
        }

        [Test]
        public void RecordUpmOwner_UserAlreadyWroteIt_KeepsEntryAndRecordsOriginal()
        {
            PackageManagerStateData state = NewState();

            UpmEntryOutcome outcome = PackageManagerStateRegistry.RecordUpmOwner(state, ManifestWithUserEntry,
                Upm("com.cysharp.unitask", "9.9.9"), "blackboard", out string newText);

            Assert.AreEqual(UpmEntryOutcome.KeptUserEntry, outcome);
            Assert.AreEqual(ManifestWithUserEntry, newText, "用户写的行一个字都不许动");
            Assert.IsTrue(state.upmOwnership[0].preexisting);
            Assert.AreEqual("2.5.11", state.upmOwnership[0].originalValue);
        }

        [Test]
        public void RecordUpmOwner_SecondOwner_CountsBothWithoutSecondWrite()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out string afterFirst);

            UpmEntryOutcome outcome = PackageManagerStateRegistry.RecordUpmOwner(state, afterFirst,
                Upm("com.cysharp.zstring", "2.6.0"), "skill-tree", out string afterSecond);

            Assert.AreEqual(UpmEntryOutcome.Retained, outcome);
            Assert.AreEqual(afterFirst, afterSecond, "同一依赖的第二个使用者不该再写一遍盘");
            CollectionAssert.AreEqual(new[] { "blackboard", "skill-tree" }, state.upmOwnership[0].owners);
        }

        [Test]
        public void RecordUpmOwner_SameOwnerTwice_DoesNotDuplicateOwnerList()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out string afterFirst);
            PackageManagerStateRegistry.RecordUpmOwner(state, afterFirst, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out _);

            Assert.AreEqual(1, state.upmOwnership[0].owners.Count);
        }

        [Test]
        public void RecordUpmOwner_LowerRequirement_DoesNotDowngradeWrittenVersion()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out string afterFirst);

            UpmEntryOutcome outcome = PackageManagerStateRegistry.RecordUpmOwner(state, afterFirst,
                Upm("com.cysharp.zstring", "2.5.0"), "skill-tree", out string afterSecond);

            Assert.AreEqual(UpmEntryOutcome.Retained, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(afterSecond, "com.cysharp.zstring", out string version));
            Assert.AreEqual("2.6.0", version, "低要求不得把已写的高版本降下去");
        }

        [Test]
        public void RecordUpmOwner_HigherRequirement_RewritesOwnedLine()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.5.0"),
                "blackboard", out string afterFirst);

            UpmEntryOutcome outcome = PackageManagerStateRegistry.RecordUpmOwner(state, afterFirst,
                Upm("com.cysharp.zstring", "2.6.0"), "skill-tree", out string afterSecond);

            Assert.AreEqual(UpmEntryOutcome.Updated, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(afterSecond, "com.cysharp.zstring",
                out string version));
            Assert.AreEqual("2.6.0", version, "同键只留版本最高的那条要求");
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(afterSecond, "com.unity.cinemachine", out _),
                "邻居条目不能丢");
        }

        [Test]
        public void ReleaseUpmOwner_PluginNotRecordedAsOwner_LeavesLedgerAndLine()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "skill-tree", out string text);

            UpmEntryOutcome outcome = PackageManagerStateRegistry.ReleaseUpmOwner(state, text,
                "com.cysharp.zstring", "blackboard", out string released);

            Assert.AreEqual(UpmEntryOutcome.NotFound, outcome);
            Assert.AreEqual(text, released, "无关卸载不该改工程 manifest 的文本");
            Assert.AreEqual(1, state.upmOwnership.Count, "别人的归属记录不该被抹掉");
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(released, "com.cysharp.zstring", out _));
        }

        [Test]
        public void ReleaseUpmOwner_LedgerWithNoOwners_LeavesLineAlone()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "skill-tree", out string text);
            state.upmOwnership[0].owners.Clear();

            UpmEntryOutcome outcome = PackageManagerStateRegistry.ReleaseUpmOwner(state, text,
                "com.cysharp.zstring", "blackboard", out string released);

            Assert.AreEqual(UpmEntryOutcome.NotFound, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(released, "com.cysharp.zstring", out _),
                "owners 空也不能替别人删那一行");
        }

        [Test]
        public void FindOwnership_ReturnsRecordForPackageIdAndNullOtherwise()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out _);

            UpmOwnershipData record = PackageManagerStateRegistry.FindOwnership(state, "com.cysharp.zstring");
            Assert.IsNotNull(record, "窗口标归属要靠这条查询");
            Assert.AreEqual("blackboard", record.owners[0]);
            Assert.IsNull(PackageManagerStateRegistry.FindOwnership(state, "com.nope"));
        }

        [Test]
        public void ReleaseUpmOwner_OtherOwnerRemains_KeepsEntry()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out string text);
            PackageManagerStateRegistry.RecordUpmOwner(state, text, Upm("com.cysharp.zstring", "2.6.0"),
                "skill-tree", out text);

            UpmEntryOutcome outcome = PackageManagerStateRegistry.ReleaseUpmOwner(state, text,
                "com.cysharp.zstring", "blackboard", out string newText);

            Assert.AreEqual(UpmEntryOutcome.Retained, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(newText, "com.cysharp.zstring", out _), "还有使用者时不能删");
        }

        [Test]
        public void ReleaseUpmOwner_LastOwnerRemovesOwnedEntry()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestClean, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out string text);

            UpmEntryOutcome outcome = PackageManagerStateRegistry.ReleaseUpmOwner(state, text,
                "com.cysharp.zstring", "blackboard", out string newText);

            Assert.AreEqual(UpmEntryOutcome.Removed, outcome);
            Assert.IsFalse(ProjectManifestUtility.TryGetDependency(newText, "com.cysharp.zstring", out _));
            Assert.IsEmpty(state.upmOwnership);
        }

        [Test]
        public void ReleaseUpmOwner_PreexistingEntrySurvivesLastOwner()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestWithUserEntry, Upm("com.cysharp.unitask", "2.5.11"),
                "blackboard", out string text);

            UpmEntryOutcome outcome = PackageManagerStateRegistry.ReleaseUpmOwner(state, text,
                "com.cysharp.unitask", "blackboard", out string newText);

            Assert.AreEqual(UpmEntryOutcome.KeptUserEntry, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(newText, "com.cysharp.unitask", out string version));
            Assert.AreEqual("2.5.11", version, "用户手写的条目卸载后必须还在，且值不变");
        }

        [Test]
        public void ReleaseUpmOwner_UnknownPackage_ReturnsNotFound()
        {
            PackageManagerStateData state = NewState();
            UpmEntryOutcome outcome = PackageManagerStateRegistry.ReleaseUpmOwner(state, ManifestClean,
                "com.nope", "blackboard", out string newText);

            Assert.AreEqual(UpmEntryOutcome.NotFound, outcome);
            Assert.AreEqual(ManifestClean, newText);
        }

        [Test]
        public void SaveThenLoad_RoundTripsOwnershipAndPreexistingFlag()
        {
            PackageManagerStateData state = NewState();
            PackageManagerStateRegistry.RecordUpmOwner(state, ManifestWithUserEntry, Upm("com.cysharp.unitask", "2.5.11"),
                "blackboard", out string text);
            PackageManagerStateRegistry.RecordUpmOwner(state, text, Upm("com.cysharp.zstring", "2.6.0"),
                "blackboard", out text);
            PackageManagerStateRegistry.UpsertInstalled(state, new InstalledPluginData
            {
                id = "blackboard", version = "0.1.0", commit = "abc123", function = "Data",
                tags = new[] { "MOIRAI_BLACKBOARD" }
            });

            PackageManagerStateRegistry.Save(state);

            PackageManagerStateData reloaded = PackageManagerStateRegistry.Load();
            Assert.AreEqual(1, reloaded.installed.Count);
            Assert.AreEqual("abc123", reloaded.installed[0].commit);
            CollectionAssert.AreEqual(new[] { "MOIRAI_BLACKBOARD" }, reloaded.installed[0].tags);
            Assert.AreEqual(2, reloaded.upmOwnership.Count);

            UpmOwnershipData userEntry = null;
            UpmOwnershipData owned = null;
            foreach (UpmOwnershipData item in reloaded.upmOwnership)
            {
                if (item.packageId == "com.cysharp.unitask") userEntry = item;
                if (item.packageId == "com.cysharp.zstring") owned = item;
            }

            Assert.IsNotNull(userEntry);
            Assert.IsTrue(userEntry.preexisting, "preexisting 标记必须过得了往返");
            Assert.AreEqual("2.5.11", userEntry.originalValue);
            Assert.IsNotNull(owned);
            Assert.IsFalse(owned.preexisting);
            CollectionAssert.AreEqual(new[] { "blackboard" }, owned.owners);
        }

        [Test]
        public void TryLoad_CorruptFile_PreservesCopyAndReturnsDefault()
        {
            File.WriteAllText(_statePath, "{ this is not json");

            Assert.IsFalse(PackageManagerStateRegistry.TryLoad(out PackageManagerStateData state, out string failure));
            Assert.IsNotNull(state);
            Assert.AreEqual(0, state.installed.Count);
            Assert.IsNotEmpty(failure);
            Assert.AreEqual(1, Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_statePath) + ".corrupt-*").Length,
                "损坏原文必须留副本，不许就地覆写");
        }

        [Test]
        public void RecordUpmOwner_EmptyManifestText_ReturnsNotFoundWithoutTouchingLedger()
        {
            PackageManagerStateData state = NewState();
            UpmEntryOutcome outcome = PackageManagerStateRegistry.RecordUpmOwner(state, string.Empty,
                Upm("com.cysharp.zstring", "2.6.0"), "blackboard", out string newText);

            Assert.AreEqual(UpmEntryOutcome.NotFound, outcome);
            Assert.AreEqual(string.Empty, newText);
            Assert.IsEmpty(state.upmOwnership);
        }
    }
}
