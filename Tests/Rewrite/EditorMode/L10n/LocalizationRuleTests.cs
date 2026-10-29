using System;
using Moirai.Atropos.L10n.Internal;
using NUnit.Framework;

namespace L10n
{
    public sealed class LocalizationRuleTests
    {
        [TestCase("ar", true)]
        [TestCase("AR", true)]
        [TestCase("he", true)]
        [TestCase("fa", false)]
        [TestCase("ur", false)]
        [TestCase("en", false)]
        public void Rtl_OnlyAllowsArabicAndHebrew(string language, bool expected)
        {
            Assert.AreEqual(expected, RtlAllowlist.IsRtl(language));
        }

        [TestCase("en", 1, "one")]
        [TestCase("en", 2, "other")]
        [TestCase("zh", 1, "other")]
        [TestCase("ja", 1, "other")]
        public void Plural_SelectsByLanguage(string language, long count, string expected)
        {
            Assert.AreEqual(expected, PluralRules.Select(language, count, "one", "other"));
        }
    }
}
