using NUnit.Framework;
using Moirai.Atropos.Editor;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 工程 manifest 依赖块行级增删的用例集。
    /// </summary>
    internal sealed class ProjectManifestUtilityTests
    {
        private const string WithEntry = @"{
  ""dependencies"": {
    ""com.unity.cinemachine"": ""3.1.7"",
    ""com.cysharp.unitask"": ""2.5.11""
  }
}";

        private const string EmptyBlock = @"{
  ""dependencies"": {
  }
}";

        [Test]
        public void TryGetDependency_ExistingKey_ReturnsValue()
        {
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(WithEntry, "com.cysharp.unitask", out string version));
            Assert.AreEqual("2.5.11", version);
        }

        [Test]
        public void TryGetDependency_AbsentKey_ReturnsFalse()
        {
            Assert.IsFalse(ProjectManifestUtility.TryGetDependency(WithEntry, "com.nope", out _));
        }

        [Test]
        public void SetDependency_AbsentKey_InsertsBeforeClosingBrace()
        {
            string text = ProjectManifestUtility.SetDependency(WithEntry, "com.tuyoogame.yooasset", "3.0.6",
                takeOverExisting: false, out UpmEntryOutcome outcome);
            Assert.AreEqual(UpmEntryOutcome.Added, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(text, "com.tuyoogame.yooasset", out string version));
            Assert.AreEqual("3.0.6", version);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(text, "com.unity.cinemachine", out _), "原有条目不能被弄丢");
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(text, "com.cysharp.unitask", out _), "原有条目不能被弄丢");
        }

        [Test]
        public void SetDependency_UserOwnedKey_KeepsEntryVerbatim()
        {
            string text = ProjectManifestUtility.SetDependency(WithEntry, "com.cysharp.unitask", "9.9.9",
                takeOverExisting: false, out UpmEntryOutcome outcome);
            Assert.AreEqual(UpmEntryOutcome.KeptUserEntry, outcome);
            Assert.AreEqual(WithEntry, text, "用户手写的行一个字都不许动");
        }

        [Test]
        public void SetDependency_TakeOverOwnedKey_UpdatesInPlace()
        {
            string text = ProjectManifestUtility.SetDependency(WithEntry, "com.cysharp.unitask", "2.6.0",
                takeOverExisting: true, out UpmEntryOutcome outcome);
            Assert.AreEqual(UpmEntryOutcome.Updated, outcome);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(text, "com.cysharp.unitask", out string version));
            Assert.AreEqual("2.6.0", version);
            StringAssert.Contains("com.unity.cinemachine", text);
        }

        [Test]
        public void SetDependency_EmptyBlock_ProducesNoTrailingComma()
        {
            string text = ProjectManifestUtility.SetDependency(EmptyBlock, "com.a.b", "1.0.0",
                takeOverExisting: false, out _);
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(text, "com.a.b", out _));
            StringAssert.DoesNotContain(",\n  }", text, "空块插入后不得留尾随逗号");
        }

        [Test]
        public void SetDependency_SameVersionTwice_Idempotent()
        {
            string once = ProjectManifestUtility.SetDependency(WithEntry, "com.a.b", "1.0.0",
                takeOverExisting: false, out _);
            string twice = ProjectManifestUtility.SetDependency(once, "com.a.b", "1.0.0",
                takeOverExisting: true, out UpmEntryOutcome outcome);
            Assert.AreEqual(UpmEntryOutcome.Updated, outcome);
            Assert.AreEqual(once, twice, "同版本重写两遍必须同文");
        }

        [Test]
        public void RemoveDependency_LastEntryStripsDanglingComma()
        {
            string text = ProjectManifestUtility.RemoveDependency(WithEntry, "com.cysharp.unitask",
                out UpmEntryOutcome outcome);
            Assert.AreEqual(UpmEntryOutcome.Removed, outcome);
            Assert.IsFalse(ProjectManifestUtility.TryGetDependency(text, "com.cysharp.unitask", out _));
            Assert.IsTrue(ProjectManifestUtility.TryGetDependency(text, "com.unity.cinemachine", out _), "邻居条目要留下");
            StringAssert.DoesNotContain(",\n  }", text, "删掉最后一行后邻居的尾随逗号必须剥掉");
        }

        [Test]
        public void RemoveDependency_AbsentKey_ReportsNotFound()
        {
            string text = ProjectManifestUtility.RemoveDependency(WithEntry, "com.nope", out UpmEntryOutcome outcome);
            Assert.AreEqual(UpmEntryOutcome.NotFound, outcome);
            Assert.AreEqual(WithEntry, text);
        }

        [Test]
        public void SetDependency_PreservesContentOutsideDependenciesBlock()
        {
            const string withScopes = @"{
  ""scopedRegistries"": [
    { ""name"": ""Open UPM"", ""url"": ""https://x"", ""scopes"": [""com.moirai""] }
  ],
  ""dependencies"": {
    ""com.a.b"": ""1.0.0""
  }
}";
            string text = ProjectManifestUtility.SetDependency(withScopes, "com.c.d", "2.0.0",
                takeOverExisting: false, out _);
            StringAssert.Contains("scopedRegistries", text);
            StringAssert.Contains("com.moirai", text);
        }
    }
}
