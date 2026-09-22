using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 一批本地化词条：<b>语言头与词条同生死</b>。
    /// <para>取代「语言列表取自一张全局静态注册表、词条列序靠反射字段声明序」的跨文件隐含约定：
    /// 数据源自报它产出的语言与列顺序，接收方只需校验二者等长，不必再猜求值顺序。</para>
    /// </summary>
    internal sealed class LocalizationTextBatch
    {
        /// <summary>空批（数据未就绪）。</summary>
        public static readonly LocalizationTextBatch Empty = new LocalizationTextBatch(
            Array.Empty<Language>(), new Dictionary<string, List<string>>(), "none");

        /// <summary>本批词条的语言，顺序即 <see cref="Strings"/> 中列的下标顺序。</summary>
        public Language[] Languages { get; }

        /// <summary>key → 语言列（与 <see cref="Languages"/> 等长）。</summary>
        public Dictionary<string, List<string>> Strings { get; }

        /// <summary>来源标识（如 <c>config-table</c>、<c>editor-preview</c>），只用于诊断归因。</summary>
        public string SourceId { get; }

        /// <summary>全部语言列的译文总字符数（常驻规模下限的分子）。</summary>
        public long ResidentChars { get; }

        public LocalizationTextBatch(IReadOnlyList<Language> languages, Dictionary<string, List<string>> strings, string sourceId)
        {
            Languages = languages switch
            {
                null => Array.Empty<Language>(),
                Language[] array => array,
                _ => new List<Language>(languages).ToArray(),
            };
            Strings = strings ?? new Dictionary<string, List<string>>();
            SourceId = string.IsNullOrEmpty(sourceId) ? "unknown" : sourceId;

            long residentChars = 0;
            foreach (var pair in Strings)
            {
                var columns = pair.Value;
                if (columns == null) continue;

                for (var i = 0; i < columns.Count; i++)
                {
                    residentChars += columns[i]?.Length ?? 0;
                }
            }

            ResidentChars = residentChars;
        }
    }

    /// <summary>
    /// 一层运行时词条覆盖：按语言分格，覆盖优先于批内译文。
    /// <para>刻意不做「清空后重建」的整表替换语义：那种写法在入参为空时会把全部文案抹掉且不带告警，
    /// 而覆盖层的正确用法是叠加——基础词条永远留在原地。</para>
    /// </summary>
    internal sealed class LocalizationOverlay
    {
        /// <summary>来源标识（远程运营 / QA 强改 / 热补丁），最后注册的一层优先。</summary>
        public string SourceId { get; }

        public Dictionary<Language, Dictionary<string, string>> ByLanguage { get; } =
            new Dictionary<Language, Dictionary<string, string>>();

        public int Count { get; private set; }

        public LocalizationOverlay(string sourceId)
        {
            SourceId = string.IsNullOrEmpty(sourceId) ? "overlay" : sourceId;
        }

        public void Add(Language language, string key, string text)
        {
            if (language == null || string.IsNullOrEmpty(key)) return;

            if (!ByLanguage.TryGetValue(language, out var cells))
            {
                cells = new Dictionary<string, string>();
                ByLanguage[language] = cells;
            }

            if (cells.ContainsKey(key))
            {
                cells[key] = text;
                return;
            }

            cells.Add(key, text);
            Count++;
        }

        /// <summary>取覆盖译文；未覆盖或覆盖为空/仅空白时返回 <c>null</c>（空覆盖等于「不覆盖」而非「覆盖成空」）。</summary>
        public bool TryGet(Language language, string key, out string text)
        {
            text = null;
            if (ByLanguage.Count == 0 || language == null) return false;
            if (!ByLanguage.TryGetValue(language, out var cells)) return false;
            if (!cells.TryGetValue(key, out var value)) return false;
            if (string.IsNullOrWhiteSpace(value)) return false;

            text = value;
            return true;
        }
    }
}
