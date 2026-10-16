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
            PluginUpmDependency[] upmDependencies = null, string minFrameworkVersion = "1.1.0",
            string maxUnityVersion = null, string maxFrameworkVersion = null)
        {
            return new PluginManifest
            {
                id = id,
                name = "Blackboard",
                function = "Data",
                version = version,
                minUnityVersion = minUnityVersion,
                minFrameworkVersion = minFrameworkVersion,
                maxUnityVersion = maxUnityVersion,
                maxFrameworkVersion = maxFrameworkVersion,
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

        /// <summary>上限是可选字段：不写=无上限，写了就必须三段可读，否则整条清单判非。</summary>
        [Test]
        [TestCase("6.2")]
        [TestCase("beyond")]
        [TestCase("")]
        public void Validate_BlankButMalformedCeiling_StillMeansUnlimited(string ceiling)
        {
            // 空串=不写；非空而读不懂=拒。两条必须分得开，否则 "2022.3" 这种半截写法会静默变成无上限
            if (string.IsNullOrEmpty(ceiling))
            {
                Assert.IsTrue(ValidateOnly(Build(maxUnityVersion: ceiling), out List<string> empty), string.Join(" | ", empty));
                return;
            }

            Assert.IsFalse(ValidateOnly(Build(maxUnityVersion: ceiling), out List<string> errors));
            StringAssert.Contains("maxUnityVersion", string.Join(" | ", errors));
        }

        [Test]
        [TestCase("2022.3.0")]
        [TestCase("2021.0.0")]
        public void Validate_CeilingNotAboveFloor_RejectedAsEmptyRange(string ceiling)
        {
            Assert.IsFalse(ValidateOnly(Build(minUnityVersion: "2022.3.0", maxUnityVersion: ceiling), out List<string> errors));
            StringAssert.Contains("区间为空", string.Join(" | ", errors));
        }

        [Test]
        public void Validate_FrameworkCeilingBelowFloor_RejectedAsEmptyRange()
        {
            Assert.IsFalse(ValidateOnly(Build(minFrameworkVersion: "1.1.0", maxFrameworkVersion: "1.1.0"), out List<string> errors));
            StringAssert.Contains("maxFrameworkVersion", string.Join(" | ", errors));
        }

        /// <summary>区间判定的边界：等于上限即已停止提供；上限之下照常放行（同一方法的正对照）。</summary>
        [Test]
        public void SatisfiesUnityVersion_CeilingBoundary_IsHalfOpen()
        {
            PluginManifest capped = Build(maxUnityVersion: "6000.3.0");

            Assert.IsTrue(capped.SatisfiesUnityVersion(new PluginVersion(6000, 2, 99)), "上限之下该放行");
            Assert.IsFalse(capped.SatisfiesUnityVersion(new PluginVersion(6000, 3, 0)), "等于上限即不再提供");
            Assert.IsTrue(Build().SatisfiesUnityVersion(new PluginVersion(9999, 0, 0)), "没写上限就是无上限");
        }

        [Test]
        public void SatisfiesFrameworkVersion_CeilingBoundary_IsHalfOpen()
        {
            PluginManifest capped = Build(maxFrameworkVersion: "1.2.0");

            Assert.IsTrue(capped.SatisfiesFrameworkVersion(new PluginVersion(1, 1, 9)));
            Assert.IsFalse(capped.SatisfiesFrameworkVersion(new PluginVersion(1, 2, 0)));
        }

        /// <summary>地板读不懂仍按不满足处理（与加上限之前的判据同形），不能因为解析失败就放行。</summary>
        [Test]
        public void SatisfiesUnityVersion_MalformedFloor_IsNotSatisfied()
        {
            Assert.IsFalse(Build(minUnityVersion: "two-thousand").SatisfiesUnityVersion(new PluginVersion(2022, 3, 0)));
        }

        [Test]
        public void TryGetUnityVersionViolation_CeilingMessage_QuotesTheStoppedVersion()
        {
            PluginManifest capped = Build(maxUnityVersion: "6000.3.0");

            PluginManifest.VersionViolation kind = capped.TryGetUnityVersionViolation(new PluginVersion(6000, 3, 26),
                out PluginManifest.VersionViolation outKind, out string message);

            Assert.AreEqual(PluginManifest.VersionViolation.CeilingExceeded, kind);
            Assert.AreEqual(PluginManifest.VersionViolation.CeilingExceeded, outKind);
            StringAssert.Contains("不再支持", message);
            StringAssert.Contains("6000.3.0", message);
            StringAssert.Contains("6000.3.26", message);
        }

        /// <summary>
        /// 上限必须真经解析通道绑上字段：JsonUtility 不认的名字会静默留 null，
        /// 那时整条上限判据永远判"无限制"，功能看着在却一次也不生效。
        /// </summary>
        [Test]
        public void TryParse_ManifestWithCeilings_BindsBothUpperBounds()
        {
            const string json = @"{
                ""id"": ""unity-logging"", ""name"": ""Unity Logging 日志后端"", ""function"": ""日志"",
                ""description"": ""d"", ""author"": ""a"", ""version"": ""1.0.0"",
                ""minUnityVersion"": ""2022.3.12"", ""maxUnityVersion"": ""6000.3.0"",
                ""minFrameworkVersion"": ""1.0.2"", ""maxFrameworkVersion"": ""2.0.0"",
                ""tags"": [], ""dependencies"": [], ""upmDependencies"": []}";

            Assert.IsTrue(PluginManifest.TryParse("unity-logging/unity-logging.plugin.json", json,
                out PluginManifest manifest, out List<string> errors), string.Join(" | ", errors));
            Assert.IsEmpty(errors);
            Assert.AreEqual("6000.3.0", manifest.maxUnityVersion);
            Assert.AreEqual("2.0.0", manifest.maxFrameworkVersion);

            PluginVersion.TryParse("6000.3.26", out PluginVersion editorOn6_3);
            Assert.IsFalse(manifest.SatisfiesUnityVersion(editorOn6_3), "6.3 的编辑器应被判超出上限");
            PluginVersion.TryParse("2022.3.12", out PluginVersion editorAtFloor);
            Assert.IsTrue(manifest.SatisfiesUnityVersion(editorAtFloor), "地板那一档仍该放行");
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

        [Test]
        public void TryParse_UnknownFields_LeaveParsedValuesUnchanged()
        {
            Assert.IsTrue(PluginManifest.TryParse(GoodPath, GoodJson, out PluginManifest plain,
                out List<string> plainErrors));

            // 多写的字段（含一个拼错的地板字段名）必须原样被忽略：既不改已认字段的取值，也不改判非条数
            string withExtras = GoodJson.Substring(0, GoodJson.Length - 1)
                + @", ""vendor"": ""someone"", ""experimental"": { ""nested"": true },"
                + @" ""minFrameworkVerion"": ""9.9.9""}";
            Assert.IsTrue(PluginManifest.TryParse(GoodPath, withExtras, out PluginManifest extra,
                out List<string> extraErrors), string.Join(" | ", extraErrors));

            Assert.AreEqual(plain.id, extra.id);
            Assert.AreEqual(plain.version, extra.version);
            Assert.AreEqual(plain.minFrameworkVersion, extra.minFrameworkVersion, "未知字段不得改变已认字段的取值");
            Assert.AreEqual(plain.dependencies.Length, extra.dependencies.Length);
            Assert.AreEqual(plain.upmDependencies[0].version, extra.upmDependencies[0].version);
            Assert.AreEqual(plainErrors.Count, extraErrors.Count);
        }

        [Test]
        [TestCase("日志", true)]
        [TestCase("Tools", true)]
        [TestCase("工具/子面", false)]
        [TestCase("bad\\name", false)]
        [TestCase("带\n换行", false)]
        [TestCase("", false)]
        public void IsValidLabel_MenuPathShapes(string label, bool expectOk)
        {
            Assert.AreEqual(expectOk, PluginManifest.IsValidLabel(label), label);
        }

        [Test]
        public void IsValidLabel_LongerThanCap_Rejects()
        {
            Assert.IsFalse(PluginManifest.IsValidLabel(new string('a', PluginManifest.MAX_LABEL_LENGTH + 1)));
        }

        [Test]
        public void TryParse_FunctionWithSeparator_ReportsLabelReason()
        {
            string json = "{\"id\":\"blackboard\",\"name\":\"Blackboard\",\"function\":\"工具/子面\"," +
                          "\"version\":\"1.0.0\",\"minUnityVersion\":\"2022.3.0\",\"minFrameworkVersion\":\"1.0.0\"}";

            Assert.IsFalse(PluginManifest.TryParse("blackboard/blackboard.plugin.json", json,
                out _, out List<string> errors));
            StringAssert.Contains("function", string.Join(" | ", errors));
        }

        private static PluginProvider GoodProvider() => new PluginProvider
        {
            providerBase = "Moirai.Atropos.Resource.ResourceServiceHandler",
            providerType = "Moirai.Atropos.Resource.YooAssetHandler",
            title = "YooAsset",
            requiresUpm = "com.tuyoogame.yooasset"
        };

        [Test]
        public void Validate_ProvidersWellFormed_SucceedsWithoutErrors()
        {
            PluginManifest candidate = Build();
            candidate.providers = new[] { GoodProvider() };
            Assert.IsTrue(ValidateOnly(candidate, out List<string> errors), string.Join(" | ", errors));
            Assert.IsEmpty(errors);
        }

        [Test]
        public void Validate_ProvidersAbsent_Succeeds()
        {
            Assert.IsTrue(ValidateOnly(Build(), out _), "providers 缺席必须放行：存量 15 份清单都没写它");
        }

        [Test]
        [TestCase("")]
        [TestCase("with/slash")]
        [TestCase("with back\\slash")]
        [TestCase("with space")]
        public void Validate_ProviderBaseMalformed_ReportsThatIndex(string providerBase)
        {
            PluginManifest candidate = Build();
            candidate.providers = new[] { new PluginProvider { providerBase = providerBase, providerType = "A.B" } };
            Assert.IsFalse(ValidateOnly(candidate, out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("providers[0].providerBase"));
        }

        [Test]
        public void Validate_ProviderTypeMissing_Reports()
        {
            PluginManifest candidate = Build();
            candidate.providers = new[] { new PluginProvider { providerBase = "A.B", providerType = "" } };
            Assert.IsFalse(ValidateOnly(candidate, out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("providers[0].providerType"));
        }

        [Test]
        [TestCase("Com.Cysharp.Unitask")]
        [TestCase("has space")]
        [TestCase("")]
        public void Validate_RequiresUpmMalformed_Reports(string requiresUpm)
        {
            PluginManifest candidate = Build();
            candidate.providers = new[] { new PluginProvider { providerBase = "A.B", providerType = "C.D", requiresUpm = requiresUpm } };
            Assert.IsFalse(ValidateOnly(candidate, out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("providers[0].requiresUpm"));
        }

        [Test]
        public void Validate_DuplicateProviderType_Reports()
        {
            PluginManifest candidate = Build();
            candidate.providers = new[] { GoodProvider(), GoodProvider() };
            Assert.IsFalse(ValidateOnly(candidate, out List<string> errors));
            Assert.That(string.Join(" | ", errors), Does.Contain("重复"));
        }

        [Test]
        public void TryParse_ProvidersRoundTrips()
        {
            const string json = @"{
                ""id"": ""yooasset"", ""name"": ""YooAsset"", ""function"": ""资源"", ""description"": ""d"",
                ""author"": ""a"", ""version"": ""0.1.0"", ""minUnityVersion"": ""2022.3.0"",
                ""minFrameworkVersion"": ""1.0.2"",
                ""providers"": [{""providerBase"":""Moirai.Atropos.Resource.ResourceServiceHandler"",
                                 ""providerType"":""Moirai.Atropos.Resource.YooAssetHandler"",
                                 ""title"":""YooAsset"",""requiresUpm"":""com.tuyoogame.yooasset""}]}";

            Assert.IsTrue(PluginManifest.TryParse("yooasset/yooasset.plugin.json", json,
                out PluginManifest manifest, out List<string> errors), string.Join(" | ", errors));
            Assert.AreEqual(1, manifest.providers.Length);
            Assert.AreEqual("Moirai.Atropos.Resource.YooAssetHandler", manifest.providers[0].providerType);
        }
    }
}
