namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// CLDR cardinal 复数规则（内置语言族的常用子集，整数口径）。
    /// <para>复数词条约定：基础 ID 加类别后缀 <c>id#zero|one|two|few|many|other</c>；
    /// 查询按「id#选中类别 → id#other → id 裸 key」回落。未收录语言一律按「仅 other」处理——
    /// 表里只要保证 <c>id#other</c> 存在，任何语言都能显示。</para>
    /// </summary>
    internal static class LocalizationPluralRules
    {
        /// <summary>
        /// 按语言 Code 解析整数数量的复数类别后缀。
        /// </summary>
        /// <param name="languageCode">语言 Code（如 en、ru、ar）；<c>null</c>/未收录按「仅 other」。</param>
        /// <param name="count">整数数量（负数的形态规则与正数一致，按绝对值判定）。</param>
        public static string ResolveCategory(string languageCode, long count)
        {
            // 负数文案（-1 apples）与正数同一形态判定
            if (count < 0) count = -count;

            switch (languageCode)
            {
                // 无形态变化
                case "zh":
                case "zh-Hans":
                case "zh-Hant":
                case "ja":
                case "ko":
                case "vi":
                case "th":
                case "id":
                case "ms":
                case "ka":
                case "tr":
                    return "other";

                // one: 0..1
                case "fr":
                case "hi":
                case "fa":
                case "az":
                    return count <= 1 ? "one" : "other";

                // 斯拉夫基数族（俄语/乌克兰语/白俄罗斯语/克罗地亚语/波斯尼亚语/塞尔维亚语）
                case "ru":
                case "uk":
                case "be":
                case "hr":
                case "bs":
                case "sr":
                    return ResolveSlavic(count);

                case "pl":
                    return ResolvePolish(count);

                // 捷克/斯洛伐克：整数只有 1 / 2..4 / 其他（many 仅用于非整数）
                case "cs":
                case "sk":
                    if (count == 1) return "one";
                    return count >= 2 && count <= 4 ? "few" : "other";

                case "ar":
                    return ResolveArabic(count);

                case "he":
                    if (count == 1) return "one";
                    if (count == 2) return "two";
                    return count > 10 && count % 10 == 0 ? "many" : "other";

                case "ro":
                    if (count == 1) return "one";
                    return count == 0 || (count % 100 >= 1 && count % 100 <= 19) ? "few" : "other";

                case "lt":
                    if (count % 10 == 1 && (count % 100 < 11 || count % 100 > 19)) return "one";
                    return count % 10 >= 2 && count % 10 <= 9 && (count % 100 < 11 || count % 100 > 19) ? "few" : "other";

                case "lv":
                    if (count % 10 == 0 || (count % 100 >= 11 && count % 100 <= 19)) return "zero";
                    return count % 10 == 1 && count % 100 != 11 ? "one" : "other";

                // 两形态族（one 当且仅当 n==1）
                case "en":
                case "de":
                case "es":
                case "it":
                case "nl":
                case "pt":
                case "no":
                case "nb":
                case "sv":
                case "da":
                case "fi":
                case "el":
                case "et":
                case "bg":
                case "ca":
                case "eu":
                case "af":
                    return count == 1 ? "one" : "other";

                // 未收录语言一律「仅 other」：两形态假设可能是语法错误，other 永远安全可读
                default:
                    return "other";
            }
        }

        private static string ResolveSlavic(long count)
        {
            var mod10 = count % 10;
            var mod100 = count % 100;

            if (mod10 == 1 && mod100 != 11) return "one";
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
            if (mod10 == 0 || (mod10 >= 5 && mod10 <= 9) || (mod100 >= 11 && mod100 <= 14)) return "many";
            return "other";
        }

        private static string ResolvePolish(long count)
        {
            if (count == 1) return "one";

            var mod10 = count % 10;
            var mod100 = count % 100;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
            if (mod10 == 0 || mod10 == 1 || (mod10 >= 5 && mod10 <= 9) || (mod100 >= 12 && mod100 <= 14)) return "many";
            return "other";
        }

        private static string ResolveArabic(long count)
        {
            if (count == 0) return "zero";
            if (count == 1) return "one";
            if (count == 2) return "two";

            var mod100 = count % 100;
            if (mod100 >= 3 && mod100 <= 10) return "few";
            return mod100 >= 11 && mod100 <= 99 ? "many" : "other";
        }
    }
}
