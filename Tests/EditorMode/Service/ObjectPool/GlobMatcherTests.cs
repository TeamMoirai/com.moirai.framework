using Moirai.Atropos.ObjectPool;
using NUnit.Framework;

namespace Service.ObjectPool
{
    /// <summary>
    /// PoolGlobMatcher 回归测试：字面量 / 单级 * / 递归 ** / 单字符 ? / 空段边界。
    /// </summary>
    public sealed class GlobMatcherTests
    {
        #region 字面量 [LITERAL]

        [Test]
        public void Literal_ExactPath_Matches()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/Prefabs/Enemy");

            Assert.IsTrue(matcher.IsValid);
            Assert.IsTrue(matcher.IsLiteralPattern);
            Assert.IsTrue(matcher.IsMatch("Assets/Prefabs/Enemy"));
        }

        [Test]
        public void Literal_DifferentPath_DoesNotMatch()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/Prefabs/Enemy");

            Assert.IsFalse(matcher.IsMatch("Assets/Prefabs/Player"));
            Assert.IsFalse(matcher.IsMatch("Assets/Prefabs/Enemy/Boss"), "literal must not span extra segments");
            Assert.IsFalse(matcher.IsMatch("Assets/Prefabs"));
            Assert.IsFalse(matcher.IsMatch("Enemy"));
        }

        [Test]
        public void Literal_EmptyOrInvalidPattern_NeverMatches()
        {
            Assert.IsFalse(PoolGlobMatcher.Compile(null).IsValid);
            Assert.IsFalse(PoolGlobMatcher.Compile(string.Empty).IsValid);
            Assert.IsFalse(PoolGlobMatcher.Compile("/").IsValid, "slashes-only yields no segments");
            Assert.IsFalse(PoolGlobMatcher.Compile(null).IsMatch("Assets/X"));
            Assert.IsFalse(PoolGlobMatcher.Compile(string.Empty).IsMatch("Assets/X"));
        }

        [Test]
        public void IsMatch_NullOrEmptyPath_ReturnsFalse()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/X");

            Assert.IsFalse(matcher.IsMatch(null));
            Assert.IsFalse(matcher.IsMatch(string.Empty));
        }

        #endregion

        #region 单级通配 * [SINGLE-LEVEL STAR]

        [Test]
        public void Star_SingleSegment_Matches()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/*/Enemy");

            Assert.IsTrue(matcher.IsMatch("Assets/Prefabs/Enemy"));
            Assert.IsTrue(matcher.IsMatch("Assets/A/Enemy"));
        }

        [Test]
        public void Star_DoesNotCrossSlash()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/*/Enemy");

            Assert.IsFalse(matcher.IsMatch("Assets/A/B/Enemy"), "* must not span multiple segments");
            Assert.IsFalse(matcher.IsMatch("Assets/Enemy"));
            Assert.IsFalse(matcher.IsMatch("Assets/Prefabs/Ally"));
        }

        [Test]
        public void Star_AsPrefixOrSuffix_Matches()
        {
            Assert.IsTrue(PoolGlobMatcher.Compile("Assets/*").IsMatch("Assets/Anything"));
            Assert.IsTrue(PoolGlobMatcher.Compile("*/Enemy").IsMatch("Anywhere/Enemy"));
            Assert.IsTrue(PoolGlobMatcher.Compile("*").IsMatch("SingleSegment"));
        }

        #endregion

        #region 递归通配 ** [RECURSIVE STAR]

        [Test]
        public void RecursiveStar_MatchesZeroOrMoreSegments()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/**");

            Assert.IsTrue(matcher.IsMatch("Assets"), "trailing ** can match empty remainder");
            Assert.IsTrue(matcher.IsMatch("Assets/Prefabs"));
            Assert.IsTrue(matcher.IsMatch("Assets/A/B/C/D"));
            Assert.IsFalse(matcher.IsMatch("Other/A"), "prefix literal must match");
        }

        [Test]
        public void RecursiveStar_InMiddle_MatchesNestedPaths()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/**/Enemy");

            Assert.IsTrue(matcher.IsMatch("Assets/Enemy"));
            Assert.IsTrue(matcher.IsMatch("Assets/Prefabs/Enemy"));
            Assert.IsTrue(matcher.IsMatch("Assets/A/B/Enemy"));
            Assert.IsFalse(matcher.IsMatch("Assets/A/B/Ally"));
            Assert.IsFalse(matcher.IsMatch("Other/Enemy"));
        }

        [Test]
        public void RecursiveStar_CollapsedDuplicates()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/**/**/Enemy");

            Assert.IsTrue(matcher.IsMatch("Assets/Enemy"));
            Assert.IsTrue(matcher.IsMatch("Assets/A/B/Enemy"));
        }

        #endregion

        #region 单字符通配 ? [SINGLE CHAR]

        [Test]
        public void QuestionMark_MatchesSingleChar()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/Fo?");

            Assert.IsTrue(matcher.IsMatch("Assets/Foo"));
            Assert.IsTrue(matcher.IsMatch("Assets/Fob"));
            Assert.IsFalse(matcher.IsMatch("Assets/Foox"), "? is exactly one char");
            Assert.IsFalse(matcher.IsMatch("Assets/Fo"));
            Assert.IsFalse(matcher.IsMatch("Assets/Foo/Bar"));
        }

        [Test]
        public void InSegmentStar_MatchesWithinSegment()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/F*");

            Assert.IsTrue(matcher.IsMatch("Assets/F"));
            Assert.IsTrue(matcher.IsMatch("Assets/Foo"));
            Assert.IsTrue(matcher.IsMatch("Assets/FooBar"));
            Assert.IsFalse(matcher.IsMatch("Assets/Bar"));
            Assert.IsFalse(matcher.IsMatch("Assets/Foo/Bar"), "in-segment * must not cross '/'");
        }

        #endregion

        #region 空段与边界 [EMPTY SEGMENTS & EDGES]

        [Test]
        public void Pattern_DoubleSlash_IsIgnored()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets//Enemy");

            Assert.IsTrue(matcher.IsMatch("Assets/Enemy"));
            Assert.IsFalse(matcher.IsMatch("Assets//Enemy"), "path empty segment is not folded");
        }

        [Test]
        public void Path_TrailingSlash_DoesNotMatchLiteral()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/Enemy");

            Assert.IsFalse(matcher.IsMatch("Assets/Enemy/"));
        }

        [Test]
        public void Path_LeadingSlash_DoesNotMatchLiteral()
        {
            PoolGlobMatcher matcher = PoolGlobMatcher.Compile("Assets/Enemy");

            Assert.IsFalse(matcher.IsMatch("/Assets/Enemy"));
        }

        #endregion

        #region IsLiteralPattern [LITERAL FLAG]

        [Test]
        public void IsLiteralPattern_TrueWithoutWildcards()
        {
            Assert.IsTrue(PoolGlobMatcher.Compile("Assets/X").IsLiteralPattern);
            Assert.IsTrue(PoolGlobMatcher.Compile("Assets/X/Y").IsLiteralPattern);
        }

        [Test]
        public void IsLiteralPattern_FalseWithAnyWildcard()
        {
            Assert.IsFalse(PoolGlobMatcher.Compile("Assets/*").IsLiteralPattern);
            Assert.IsFalse(PoolGlobMatcher.Compile("Assets/**").IsLiteralPattern);
            Assert.IsFalse(PoolGlobMatcher.Compile("Assets/Fo?").IsLiteralPattern);
            Assert.IsFalse(PoolGlobMatcher.Compile("Assets/F*").IsLiteralPattern);
        }

        [Test]
        public void DefaultMatcher_IsInvalid()
        {
            PoolGlobMatcher matcher = default;

            Assert.IsFalse(matcher.IsValid);
            Assert.IsFalse(matcher.IsLiteralPattern);
            Assert.IsFalse(matcher.IsMatch("Assets/X"));
        }

        #endregion
    }
}
