using NUnit.Framework;
using Moirai.Atropos.Editor;

namespace Editor.Tools.PackageManager
{
    /// <summary>
    /// 三段版本号解析与比较的用例集。
    /// </summary>
    internal sealed class PluginVersionTests
    {
        [Test]
        [TestCase("1.0.0", true)]
        [TestCase("22.5.11", true)]
        [TestCase("1.0", false)]
        [TestCase("1.0.0-beta", false)]
        [TestCase("1.0.0.0", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("a.b.c", false)]
        [TestCase(" 1.0.0", false)]
        [TestCase("+1.0.0", false)]
        [TestCase("-1.0.0", false)]
        [TestCase("1..0", false)]
        public void TryParse_ShapeContract_ReturnsExpectedVerdict(string text, bool expectOk)
        {
            Assert.AreEqual(expectOk, PluginVersion.TryParse(text, out _), text ?? "<null>");
        }

        [Test]
        public void TryParse_ThreeSegments_FillsValuesInOrder()
        {
            Assert.IsTrue(PluginVersion.TryParse("22.5.11", out PluginVersion parsed), "解析必须成立");
            Assert.AreEqual(22, parsed.Major);
            Assert.AreEqual(5, parsed.Minor);
            Assert.AreEqual(11, parsed.Patch);
        }

        [Test]
        [TestCase("1.0.0", "1.0.1", true)]
        [TestCase("1.1.0", "1.0.9", false)]
        [TestCase("2.0.0", "1.9.9", false)]
        [TestCase("1.0.0", "1.0.0", false)]
        public void LessThan_PatchThenMinorThenMajorOrdering(string left, string right, bool expectLess)
        {
            Assert.IsTrue(PluginVersion.TryParse(left, out PluginVersion a), left);
            Assert.IsTrue(PluginVersion.TryParse(right, out PluginVersion b), right);
            Assert.AreEqual(expectLess, a < b, $"{left} < {right}");
        }

        [Test]
        public void Comparisons_ParsedEqualVersions_MaintainEveryRelation()
        {
            Assert.IsTrue(PluginVersion.TryParse("1.2.3", out PluginVersion a), "解析必须成立");
            Assert.IsTrue(PluginVersion.TryParse("1.2.3", out PluginVersion b), "解析必须成立");
            Assert.IsTrue(a >= b);
            Assert.IsTrue(a <= b);
            Assert.IsFalse(a > b);
            Assert.IsFalse(a < b);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, default(PluginVersion), "解析成功的值不得等于未解析的默认值");
        }

        [Test]
        public void TryParse_TwiceSameText_ProducesEqualValues()
        {
            Assert.IsTrue(PluginVersion.TryParse("1.2.3", out PluginVersion a));
            Assert.IsTrue(PluginVersion.TryParse("1.2.3", out PluginVersion b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreEqual("1.2.3", a.ToString());
        }
    }
}
