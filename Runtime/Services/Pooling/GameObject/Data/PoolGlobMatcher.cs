using System.Runtime.CompilerServices;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 零分配 Glob 模式匹配器。
    /// </summary>
    internal readonly struct PoolGlobMatcher
    {
        #region 常量 [CONSTANTS]

        private const byte KIND_LITERAL = 0;
        private const byte KIND_WILD = 1;
        private const byte KIND_RECURSIVE_WILD = 2;
        private const byte KIND_PATTERN = 3;

        #endregion

        #region 字段 [FIELDS]

        private readonly byte[] _kinds;
        private readonly string[] _texts;
        private readonly int _segmentCount;
        private readonly bool _isLiteral;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取匹配器是否有效。
        /// </summary>
        public bool IsValid => _kinds != null && _segmentCount > 0;

        /// <summary>
        /// 获取是否为字面量模式（无通配符）。
        /// </summary>
        public bool IsLiteralPattern => _isLiteral;

        #endregion

        #region 构造 [CONSTRUCTOR]

        private PoolGlobMatcher(byte[] kinds, string[] texts, int segmentCount, bool isLiteral)
        {
            _kinds = kinds;
            _texts = texts;
            _segmentCount = segmentCount;
            _isLiteral = isLiteral;
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 编译模式字符串为匹配器。
        /// </summary>
        /// <param name="pattern">模式字符串。</param>
        /// <returns>编译后的匹配器。</returns>
        public static PoolGlobMatcher Compile(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return default;
            }

            int len = pattern.Length;
            int maxSegments = 1;
            for (int i = 0; i < len; i++)
            {
                if (pattern[i] == '/')
                {
                    maxSegments++;
                }
            }

            byte[] kinds = new byte[maxSegments];
            string[] texts = new string[maxSegments];
            int count = 0;
            bool hasWildcard = false;
            int segStart = 0;
            for (int i = 0; i <= len; i++)
            {
                if (i < len && pattern[i] != '/')
                {
                    continue;
                }

                if (i == segStart)
                {
                    segStart = i + 1;
                    continue;
                }

                string seg = pattern.Substring(segStart, i - segStart);
                segStart = i + 1;
                if (seg == "**")
                {
                    if (count > 0 && kinds[count - 1] == KIND_RECURSIVE_WILD)
                    {
                        continue;
                    }

                    kinds[count] = KIND_RECURSIVE_WILD;
                    texts[count] = null;
                    count++;
                    hasWildcard = true;
                }
                else if (seg == "*")
                {
                    kinds[count] = KIND_WILD;
                    texts[count] = null;
                    count++;
                    hasWildcard = true;
                }
                else if (ContainsWildcard(seg))
                {
                    kinds[count] = KIND_PATTERN;
                    texts[count] = seg;
                    count++;
                    hasWildcard = true;
                }
                else
                {
                    kinds[count] = KIND_LITERAL;
                    texts[count] = seg;
                    count++;
                }
            }

            return count == 0 ? default : new PoolGlobMatcher(kinds, texts, count, !hasWildcard);
        }

        /// <summary>
        /// 测试路径是否匹配模式。
        /// </summary>
        /// <param name="path">路径字符串。</param>
        /// <returns>是否匹配。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsMatch(string path)
        {
            return _kinds != null && _segmentCount > 0 && !string.IsNullOrEmpty(path) && MatchCore(path);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private bool MatchCore(string path)
        {
            int pathLen = path.Length;
            int patIdx = 0;
            int pathSegStart = 0;
            int starPatIdx = -1;
            int starPathSegStart = -1;

            while (pathSegStart <= pathLen)
            {
                int pathSegEnd = IndexOfSlash(path, pathSegStart);
                if (patIdx < _segmentCount)
                {
                    byte kind = _kinds[patIdx];
                    if (kind == KIND_RECURSIVE_WILD)
                    {
                        starPatIdx = patIdx;
                        starPathSegStart = pathSegStart;
                        patIdx++;
                        continue;
                    }

                    if (pathSegStart >= pathLen)
                    {
                        break;
                    }

                    if (MatchSegment(kind, _texts[patIdx], path, pathSegStart, pathSegEnd))
                    {
                        patIdx++;
                        pathSegStart = pathSegEnd < pathLen ? pathSegEnd + 1 : pathLen + 1;
                        continue;
                    }
                }

                if (starPatIdx < 0)
                {
                    break;
                }

                if (starPathSegStart >= pathLen)
                {
                    return false;
                }

                int nextSeg = IndexOfSlash(path, starPathSegStart);
                starPathSegStart = nextSeg < pathLen ? nextSeg + 1 : pathLen + 1;
                pathSegStart = starPathSegStart;
                patIdx = starPatIdx + 1;
            }

            while (patIdx < _segmentCount && _kinds[patIdx] == KIND_RECURSIVE_WILD)
            {
                patIdx++;
            }

            return patIdx >= _segmentCount && pathSegStart > pathLen;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchSegment(byte kind, string pattern, string path, int start, int end)
        {
            switch (kind)
            {
                case KIND_LITERAL:
                    return MatchLiteral(pattern, path, start, end);
                case KIND_WILD:
                    return true;
                case KIND_PATTERN:
                    return MatchPattern(pattern, path, start, end);
                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchLiteral(string literal, string path, int start, int end)
        {
            int segLen = end - start;
            if (segLen != literal.Length)
            {
                return false;
            }

            for (int i = 0; i < segLen; i++)
            {
                if (path[start + i] != literal[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchPattern(string pattern, string path, int start, int end)
        {
            int pLen = pattern.Length;
            int sLen = end - start;
            int pi = 0;
            int si = 0;
            int starPi = -1;
            int starSi = -1;
            while (si < sLen)
            {
                if (pi < pLen && (pattern[pi] == '?' || pattern[pi] == path[start + si]))
                {
                    pi++;
                    si++;
                }
                else if (pi < pLen && pattern[pi] == '*')
                {
                    starPi = pi;
                    starSi = si;
                    pi++;
                }
                else if (starPi >= 0)
                {
                    pi = starPi + 1;
                    si = ++starSi;
                }
                else
                {
                    return false;
                }
            }

            while (pi < pLen && pattern[pi] == '*')
            {
                pi++;
            }

            return pi == pLen;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int IndexOfSlash(string path, int start)
        {
            int len = path.Length;
            for (int i = start; i < len; i++)
            {
                if (path[i] == '/')
                {
                    return i;
                }
            }

            return len;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsWildcard(string segment)
        {
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (c == '*' || c == '?')
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
