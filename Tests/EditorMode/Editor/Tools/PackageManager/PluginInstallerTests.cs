using Moirai.Atropos.Editor;
using NUnit.Framework;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 引擎版本号取前三段的用例集。
    /// </summary>
    internal sealed class PluginInstallerTests
    {
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
    }
}
