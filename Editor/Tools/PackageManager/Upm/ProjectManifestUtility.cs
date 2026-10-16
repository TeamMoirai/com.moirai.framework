using System;

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 工程 <c>Packages/manifest.json</c> 的依赖块行级读写。
    /// </summary>
    /// <remarks>
    /// 只动 <c>dependencies</c> 块内的行，块外内容（含 scopedRegistries、注释、原有顺序与缩进）逐字保留。<br />
    /// 不用整对象 JSON 往返（丢注释与排版），也不用正则整档替换（会产尾随逗号）。<br />
    /// 键匹配按 Ordinal，不忽略大小写。<br />
    /// 幂等：同版本重写两遍必须同文。
    /// </remarks>
    internal static class ProjectManifestUtility
    {
        private const string DEPENDENCIES_TOKEN = "\"dependencies\"";
        private const int DEFAULT_INDENT = 4;

        /// <summary>读取依赖块内某键的版本值。</summary>
        public static bool TryGetDependency(string manifestText, string packageId, out string version)
        {
            version = null;
            if (string.IsNullOrEmpty(manifestText) || string.IsNullOrEmpty(packageId)) return false;

            string[] lines = SplitLines(manifestText, out _);
            if (!TryLocateBlock(lines, out int open, out int close)) return false;

            for (int i = open + 1; i < close; i++)
            {
                if (TryLocateEntry(lines[i], packageId, out int valueStart, out int valueEnd))
                {
                    version = lines[i].Substring(valueStart, valueEnd - valueStart);
                    return true;
                }
            }

            return false;
        }

        /// <summary>写入一条依赖。</summary>
        /// <remarks>
        /// <paramref name="takeOverExisting"/> 为假且该键已在盘上时原样返回，一个字符都不改——
        /// 这条是"用户手写条目不得被工具覆写"的实现点，归属判定由账本侧负责。
        /// </remarks>
        public static string SetDependency(string manifestText, string packageId, string version,
            bool takeOverExisting, out UpmEntryOutcome outcome)
        {
            outcome = UpmEntryOutcome.NotFound;
            if (string.IsNullOrEmpty(manifestText) || string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(version))
                return manifestText;

            string[] lines = SplitLines(manifestText, out bool crlf);
            if (!TryLocateBlock(lines, out int open, out int close)) return manifestText;

            for (int i = open + 1; i < close; i++)
            {
                if (!TryLocateEntry(lines[i], packageId, out int valueStart, out int valueEnd)) continue;

                string current = lines[i].Substring(valueStart, valueEnd - valueStart);
                if (!takeOverExisting)
                {
                    outcome = UpmEntryOutcome.KeptUserEntry;
                    return manifestText;
                }

                if (current == version)
                {
                    outcome = UpmEntryOutcome.Updated;
                    return manifestText;
                }

                lines[i] = lines[i].Substring(0, valueStart) + version + lines[i].Substring(valueEnd);
                outcome = UpmEntryOutcome.Updated;
                return JoinLines(lines, crlf);
            }

            int indent = ResolveIndent(lines, open, close);
            string entry = new string(' ', indent) + "\"" + packageId + "\": \"" + version + "\"";

            int lastEntry = close - 1;
            while (lastEntry > open && lines[lastEntry].Trim().Length == 0) lastEntry--;

            if (lastEntry > open && lines[lastEntry].Trim().Length > 0)
            {
                if (!lines[lastEntry].TrimEnd().EndsWith(","))
                    lines[lastEntry] = lines[lastEntry].TrimEnd() + ",";
            }

            var rebuilt = new string[lines.Length + 1];
            Array.Copy(lines, 0, rebuilt, 0, close);
            rebuilt[close] = entry;
            Array.Copy(lines, close, rebuilt, close + 1, lines.Length - close);

            outcome = UpmEntryOutcome.Added;
            return JoinLines(rebuilt, crlf);
        }

        /// <summary>删除一条依赖。</summary>
        public static string RemoveDependency(string manifestText, string packageId, out UpmEntryOutcome outcome)
        {
            outcome = UpmEntryOutcome.NotFound;
            if (string.IsNullOrEmpty(manifestText) || string.IsNullOrEmpty(packageId)) return manifestText;

            string[] lines = SplitLines(manifestText, out bool crlf);
            if (!TryLocateBlock(lines, out int open, out int close)) return manifestText;

            int hit = -1;
            for (int i = open + 1; i < close; i++)
            {
                if (TryLocateEntry(lines[i], packageId, out _, out _)) { hit = i; break; }
            }

            if (hit < 0) return manifestText;

            var rebuilt = new string[lines.Length - 1];
            Array.Copy(lines, 0, rebuilt, 0, hit);
            Array.Copy(lines, hit + 1, rebuilt, hit, lines.Length - hit - 1);

            int newClose = close - 1;
            int lastEntry = newClose - 1;
            while (lastEntry > open && rebuilt[lastEntry].Trim().Length == 0) lastEntry--;

            bool removedWasLastEntry = hit == LastEntryIndex(lines, open, close);
            if (removedWasLastEntry && lastEntry > open && rebuilt[lastEntry].Trim().Length > 0)
            {
                string trimmed = rebuilt[lastEntry].TrimEnd();
                if (trimmed.EndsWith(","))
                    rebuilt[lastEntry] = trimmed.Substring(0, trimmed.Length - 1);
            }

            outcome = UpmEntryOutcome.Removed;
            return JoinLines(rebuilt, crlf);
        }

        private static int LastEntryIndex(string[] lines, int open, int close)
        {
            for (int i = close - 1; i > open; i--)
            {
                if (lines[i].Trim().Length > 0) return i;
            }

            return open;
        }

        private static string[] SplitLines(string text, out bool crlf)
        {
            crlf = text.Contains("\r\n");
            string unified = crlf ? text.Replace("\r\n", "\n") : text;
            return unified.Split('\n');
        }

        private static string JoinLines(string[] lines, bool crlf)
        {
            return string.Join(crlf ? "\r\n" : "\n", lines);
        }

        private static bool TryLocateBlock(string[] lines, out int open, out int close)
        {
            open = -1;
            close = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(DEPENDENCIES_TOKEN)) continue;
                if (!lines[i].Contains("{")) continue;
                open = i;
                break;
            }

            if (open < 0) return false;

            for (int i = open + 1; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("}"))
                {
                    close = i;
                    return true;
                }
            }

            return false;
        }

        private static bool TryLocateEntry(string line, string packageId, out int valueStart, out int valueEnd)
        {
            valueStart = -1;
            valueEnd = -1;

            string trimmed = line.TrimStart();
            string key = "\"" + packageId + "\"";
            if (!trimmed.StartsWith(key, StringComparison.Ordinal)) return false;

            int colon = trimmed.IndexOf(':', key.Length);
            if (colon < 0) return false;

            int openQuote = trimmed.IndexOf('"', colon + 1);
            if (openQuote < 0) return false;

            valueStart = trimmed.IndexOf('"', openQuote + 1);
            if (valueStart < 0) return false;

            valueEnd = valueStart;
            valueStart = openQuote + 1;

            int leading = line.Length - trimmed.Length;
            valueStart += leading;
            valueEnd += leading;
            return true;
        }

        private static int ResolveIndent(string[] lines, int open, int close)
        {
            for (int i = open + 1; i < close; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                int indent = 0;
                while (indent < lines[i].Length && lines[i][indent] == ' ') indent++;
                return indent;
            }

            return DEFAULT_INDENT;
        }
    }
}
