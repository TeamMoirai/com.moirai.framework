using System.Collections.Generic;
using NUnit.Framework;
using Moirai.Atropos.Editor;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 插件清单校验判据与解析通道的用例集。
    /// </summary>
    internal sealed class PluginManifestTests
    {
        private const string GoodJson = @"{
            ""id"": ""blackboard"", ""name"": ""Blackboard"", ""function"": ""Data"",
            ""description"": ""d"", ""author"": ""a"", ""version"": ""0.1.0"",
            ""minUnityVersion"": ""2022.3.0"", ""minFrameworkVersion"": ""1.1.0"", ""tags"": [""MOIRAI_BLACKBOARD""],
            ""dependencies"": [{""id"":""system-extensions"",""version"":""1.0.1"",""versionOperator"":"">=""}],
            ""upmDependencies"": [{""id"":""com.cysharp.unitask"",""version"":""2.5.11""}]}";

        private const string GoodPath = "blackboard/blackboard.plugin.json";

        private static PluginManifest Build(string id = "blackboard", string version = "0.1.0",
            string minUnityVersion = "2022.3.0", PluginDependency[] dependencies = null,
            PluginUpmDependency[] upmDependencies = null, string minFrameworkVersion = "1.1.0")
        {
            return new PluginManifest
            {
                id = id,
                name = "Blackboard",
                function = "Data",
                version = version,
                minUnityVersion = minUnityVersion,
                minFrameworkVersion = minFrameworkVersion,
                dependencies = dependencies,
                upmDependencies = upmDependencies
            };
        }

        private static bool ValidateOnly(PluginManifest candidate, out List<string> errors)
        {
            errors = new List<string>();
            return PluginManifest.Validate(candidate, "blackboard/blackboard.plugin.json", errors);
        }

        [Test]
        public void Validate_GoodCandidate_SucceedsWithoutErrors()
        {
            Assert.IsTrue(ValidateOnly(Build(), out List<string> errors), string.Join(" | ", errors));
            Assert.IsEmpty(errors);
        }

        [Test]
        public void Validate_IdMismatchedWithFolder_ReportsSourcePath()
        {
            Assert.IsFalse(ValidateOnly(Build(id: "board"), out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("blackboard/blackboard.plugin.json"));
        }

        [Test]
        [TestCase("Bad Id")]
        [TestCase("UPPER")]
        [TestCase("../escape")]
        [TestCase("with/slash")]
        [TestCase(".hidden")]
        [TestCase("")]
        [TestCase(null)]
        public void IsValidId_CharsetAndLeadingDot_Rejects(string candidate)
        {
            Assert.IsFalse(PluginManifest.IsValidId(candidate), candidate ?? "<null>");
        }

        [Test]
        public void IsValidId_SlugWithDotDashUnderscore_Accepts()
        {
            Assert.IsTrue(PluginManifest.IsValidId("a.b-c_1"));
        }

        [Test]
        [TestCase("1.0")]
        [TestCase("1.0.0-beta")]
        [TestCase("")]
        [TestCase(null)]
        public void Validate_BadVersionShapes_ReportsEach(string badVersion)
        {
            Assert.IsFalse(ValidateOnly(Build(version: badVersion), out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("version"));
        }

        [Test]
        [TestCase("~")]
        [TestCase("=>")]
        [TestCase("")]
        [TestCase(null)]
        public void Validate_UnknownVersionOperator_ReportsLiteral(string badOperator)
        {
            var dependencies = new[]
            {
                new PluginDependency { id = "system-extensions", version = "1.0.0", versionOperator = badOperator }
            };

            Assert.IsFalse(ValidateOnly(Build(dependencies: dependencies), out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("versionOperator"));
        }

        [Test]
        public void Validate_NullArraySlot_ReportsIndex()
        {
            Assert.IsFalse(ValidateOnly(Build(dependencies: new PluginDependency[1]), out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("dependencies[0]"));
        }

        [Test]
        public void SatisfiesUnityVersion_BelowFloor_ReturnsFalse()
        {
            PluginManifest manifest = Build();
            PluginVersion.TryParse("2021.3.0", out PluginVersion older);
            PluginVersion.TryParse("2022.3.0", out PluginVersion same);
            PluginVersion.TryParse("6000.3.0", out PluginVersion newer);

            Assert.IsFalse(manifest.SatisfiesUnityVersion(older));
            Assert.IsTrue(manifest.SatisfiesUnityVersion(same), "等于地板应满足");
            Assert.IsTrue(manifest.SatisfiesUnityVersion(newer));
        }

        [Test]
        public void SatisfiesFrameworkVersion_BelowFloor_ReturnsFalse()
        {
            PluginManifest manifest = Build();
            PluginVersion.TryParse("1.0.0", out PluginVersion older);
            PluginVersion.TryParse("1.1.0", out PluginVersion same);
            PluginVersion.TryParse("2.0.0", out PluginVersion newer);

            Assert.IsFalse(manifest.SatisfiesFrameworkVersion(older));
            Assert.IsTrue(manifest.SatisfiesFrameworkVersion(same), "等于地板应满足");
            Assert.IsTrue(manifest.SatisfiesFrameworkVersion(newer));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        [TestCase("1.1")]
        [TestCase("1.1.0-beta")]
        public void Validate_MissingOrMalformedFrameworkFloor_ReportsError(string floor)
        {
            Assert.IsFalse(ValidateOnly(Build(minFrameworkVersion: floor), out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("minFrameworkVersion"));
        }

        [Test]
        [TestCase("1.0.1", true)]
        [TestCase("1.0.0", false)]
        [TestCase("9.9.9", true)]
        public void Matches_GreaterOrEqualRequirement_JudgesByOperator(string installedText, bool expectMatch)
        {
            var dependency = new PluginDependency
            {
                id = "system-extensions", version = "1.0.1", versionOperator = ">="
            };
            PluginVersion.TryParse(installedText, out PluginVersion installed);
            Assert.AreEqual(expectMatch, dependency.Matches(installed), installedText);
        }

        [Test]
        [TestCase("=", "1.0.1", "1.0.1", true)]
        [TestCase("=", "1.0.2", "1.0.1", false)]
        [TestCase(">", "1.0.1", "1.0.2", true)]
        [TestCase(">", "1.0.1", "1.0.1", false)]
        [TestCase("<", "1.0.1", "1.0.0", true)]
        [TestCase("<=", "1.0.1", "1.0.1", true)]
        public void Matches_EveryOperator_JudgesInstalledAgainstRequired(string op, string required, string installedText, bool expectMatch)
        {
            var dependency = new PluginDependency { id = "x", version = required, versionOperator = op };
            PluginVersion.TryParse(installedText, out PluginVersion installed);
            Assert.AreEqual(expectMatch, dependency.Matches(installed), $"{op} {required} vs {installedText}");
        }

        [Test]
        public void Matches_UnparseableRequirement_ReturnsFalse()
        {
            var dependency = new PluginDependency { id = "x", version = "1.0", versionOperator = ">=" };
            PluginVersion.TryParse("1.0.0", out PluginVersion installed);
            Assert.IsFalse(dependency.Matches(installed), "要求版本本身非法时不得当成满足");
        }

        [Test]
        public void TryParse_FacadeResolvesHandlerWithoutBoot_ParsesGoodJson()
        {
            Assert.IsTrue(PluginManifest.TryParse(GoodPath, GoodJson, out PluginManifest manifest, out List<string> errors),
                string.Join(" | ", errors));
            Assert.AreEqual("blackboard", manifest.id);
            Assert.AreEqual("Data", manifest.function);
            Assert.AreEqual(1, manifest.dependencies.Length);
            Assert.AreEqual("com.cysharp.unitask", manifest.upmDependencies[0].id);
        }

        [Test]
        public void TryParse_AbsentArrayFields_LeavesThemValidatable()
        {
            const string Minimal = @"{""id"":""solo"",""version"":""0.1.0"",""minUnityVersion"":""2022.3.0"",""minFrameworkVersion"":""1.1.0""}";
            Assert.IsTrue(PluginManifest.TryParse("solo/solo.plugin.json", Minimal,
                out PluginManifest manifest, out List<string> errors), string.Join(" | ", errors));
            Assert.AreEqual(0, manifest.dependencies?.Length ?? 0, "缺依赖字段的清单必须判为合法");
        }

        [Test]
        public void TryParse_MalformedJson_ReportsReasonWithoutThrowing()
        {
            Assert.IsFalse(PluginManifest.TryParse(GoodPath, "{ this is not json", out _, out List<string> errors));
            Assert.IsNotEmpty(errors);
        }

        [Test]
        public void TryParse_Fails_DoesNotProduceManifest()
        {
            Assert.IsFalse(PluginManifest.TryParse(GoodPath, @"{""id"":""BAD""}", out PluginManifest manifest, out _));
            Assert.IsNull(manifest, "校验失败时不得交出半成品对象");
        }
    }
}
