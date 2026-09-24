using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 换入批的头部视图：语言表、来源、规模（不含词条本体——词条由 <see cref="LocalizationStore"/> 扁平持有）。
    /// </summary>
    internal readonly struct LocalizationBatchHeader
    {
        public readonly Language[] Languages;
        public readonly string SourceId;
        public readonly long ResidentChars;
        public readonly int EntryCount;

        public LocalizationBatchHeader(Language[] languages, string sourceId, long residentChars, int entryCount)
        {
            Languages = languages;
            SourceId = sourceId;
            ResidentChars = residentChars;
            EntryCount = entryCount;
        }
    }

    /// <summary>
    /// 本地化词条存储：持有当前批的扁平词条与覆盖层，并执行「覆盖 → 指定语言 → 回退链」的取值解析。
    /// <para>词条以「key → 行索引」+ 行主序扁平数组（row × 语言数 + 列）存放，不再保留
    /// 「每词条一个 <c>List&lt;string&gt;</c> + 字典装箱」的批对象——万级词条下少一倍容器对象开销。</para>
    /// <para>与查询语义一起从处理器里拆出来，是为了让运行期数据源与编辑器预览共用同一套存储与解析
    /// （编辑器预览只是换一批数据源，不该有第二份取值逻辑）。</para>
    /// <para>本类不打日志、不做语言解析，失败一律以返回值交给调用方归因。</para>
    /// </summary>
    internal sealed class LocalizationStore
    {
        private Language[] _languages = Array.Empty<Language>();
        private Dictionary<string, int> _rowByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        // 行主序扁平词条：[row * 语言数 + column]；null/仅空白即缺译
        private string[] _cells = Array.Empty<string>();
        private string _sourceId = "none";
        private long _residentChars;
        // 换批世代号：每次成功换入/清空自增——ReloadTexts 据此判断「快照是否真的换过」
        private int _generation;

        // 覆盖层按注册顺序排列，后注册者优先；实践里同时存在的层不超过个位数，倒序直扫即可
        private readonly List<LocalizationOverlay> _overlays = new List<LocalizationOverlay>();

        /// <summary>当前生效批的头部视图（语言表/来源/规模；词条本体不随视图外泄）。</summary>
        internal LocalizationBatchHeader Batch => new LocalizationBatchHeader(_languages, _sourceId, _residentChars, _rowByKey.Count);

        /// <summary>换批世代号：每次成功换入新批或清空自增。</summary>
        internal int Generation => _generation;

        public int EntryCount => _rowByKey.Count;

        public int LanguageCount => _languages.Length;

        public long ResidentChars => _residentChars;

        /// <summary>已登记的覆盖层数量。</summary>
        public int OverlayLayerCount => _overlays.Count;

        /// <summary>
        /// 换入一批词条：校验通过后把批矩阵转写为扁平行表，批对象本体随即释放给 GC。
        /// </summary>
        /// <param name="batch">待换入的批。</param>
        /// <param name="rejectedKey">失配的首个词条 key（成功时为 null）。</param>
        /// <returns><c>true</c> 表示已换入；<c>false</c> 表示列数与语言数失配，<b>整批拒载并保留上一份可用快照</b>。</returns>
        /// <remarks>
        /// 失配之所以要整批拒载：列序错位只会表现为「显示了别的语言」而不会报错，
        /// 半损坏状态一旦被标记为已加载，就会以错语言的形式一路跑到线上。
        /// </remarks>
        public bool TryApply(LocalizationTextBatch batch, out string rejectedKey)
        {
            rejectedKey = null;
            if (batch == null || batch.Languages.Length == 0 || batch.Strings.Count == 0) return false;

            var languageCount = batch.Languages.Length;
            foreach (var pair in batch.Strings)
            {
                var columns = pair.Value;
                if (columns != null && columns.Count == languageCount) continue;

                rejectedKey = pair.Key;
                return false;
            }

            var rowByKey = new Dictionary<string, int>(batch.Strings.Count, StringComparer.Ordinal);
            var cells = new string[batch.Strings.Count * languageCount];
            var row = 0;
            foreach (var pair in batch.Strings)
            {
                rowByKey.Add(pair.Key, row);
                var columns = pair.Value;
                var baseIndex = row * languageCount;
                for (var i = 0; i < languageCount; i++)
                {
                    cells[baseIndex + i] = columns[i];
                }

                row++;
            }

            _languages = batch.Languages;
            _rowByKey = rowByKey;
            _cells = cells;
            _sourceId = batch.SourceId ?? "unknown";
            _residentChars = batch.ResidentChars;
            _generation++;
            return true;
        }

        /// <summary>清空到未加载态（关服时调用）。</summary>
        public void Clear()
        {
            _languages = Array.Empty<Language>();
            _rowByKey.Clear();
            _cells = Array.Empty<string>();
            _sourceId = "none";
            _residentChars = 0;
            _generation++;
            _overlays.Clear();
        }

        public bool HasKey(string key) => !string.IsNullOrEmpty(key) && _rowByKey.ContainsKey(key);

        /// <summary>取全部词条 key 的新列表（诊断/工具用，逐次分配）。</summary>
        public List<string> GetAllKeys()
        {
            var keys = new List<string>(_rowByKey.Count);
            foreach (var key in _rowByKey.Keys)
            {
                keys.Add(key);
            }

            return keys;
        }

        public int IndexOf(Language language)
        {
            if (language == null) return -1;

            var languages = _languages;
            for (var i = 0; i < languages.Length; i++)
            {
                if (languages[i] == language) return i;
            }

            return -1;
        }

        public Language LanguageAt(int index) => _languages[index];

        /// <summary>
        /// 按「覆盖层 → 指定语言 → 回退链」取原始译文；全部缺译时返回 <c>null</c>（由调用方决定是否露 key）。
        /// </summary>
        /// <param name="language">指定语言（覆盖层按语言分格，故与列下标一并传入）。</param>
        /// <param name="languageIndex">指定语言在批内的列下标（-1 表示该语言不在批内）。</param>
        /// <param name="fallbackLanguages">回退链语言。</param>
        /// <param name="fallbackIndices">回退链列下标，与 <paramref name="fallbackLanguages"/> 一一对应。</param>
        /// <remarks>覆盖层在<b>每一次</b>语言尝试上都先于批内译文：运营热改英语文案后，
        /// 一个缺译的法语文案回退到英语时也必须拿到改后的那版，否则覆盖只在一半路径上生效。</remarks>
        public string Resolve(string key, Language language, int languageIndex,
            IReadOnlyList<Language> fallbackLanguages, IReadOnlyList<int> fallbackIndices)
        {
            if (string.IsNullOrEmpty(key)) return null;

            var text = TryOverlay(language, key);
            if (text != null) return text;

            if (!_rowByKey.TryGetValue(key, out var row)) return null;

            text = Select(row, languageIndex);
            if (text != null) return text;

            if (fallbackIndices == null) return null;

            for (var i = 0; i < fallbackIndices.Count; i++)
            {
                text = TryOverlay(fallbackLanguages != null && i < fallbackLanguages.Count ? fallbackLanguages[i] : null, key)
                       ?? Select(row, fallbackIndices[i]);
                if (text != null) return text;
            }

            return null;
        }

        private string TryOverlay(Language language, string key)
        {
            for (var i = _overlays.Count - 1; i >= 0; i--)
            {
                if (_overlays[i].TryGet(language, key, out var covered)) return covered;
            }

            return null;
        }

        /// <summary>取指定行/列的译文；越界或为空/仅空白时视为缺译，返回 <c>null</c>。</summary>
        private string Select(int row, int column)
        {
            if (column < 0) return null;

            var index = row * _languages.Length + column;
            var text = _cells[index];
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        #region 覆盖层 [OVERLAY]

        /// <summary>取（必要时建）指定来源的覆盖层；同名来源复用，来源顺序即优先级。</summary>
        private LocalizationOverlay GetOrAddOverlay(string sourceId)
        {
            for (var i = 0; i < _overlays.Count; i++)
            {
                if (_overlays[i].SourceId == sourceId) return _overlays[i];
            }

            var overlay = new LocalizationOverlay(sourceId);
            _overlays.Add(overlay);
            return overlay;
        }

        /// <summary>
        /// 覆盖某语言下的若干词条（叠加语义：未覆盖的词条保持批内译文，空/仅空白值视为不覆盖）。
        /// </summary>
        /// <param name="sourceId">来源标识。</param>
        /// <param name="language">被覆盖的语言。</param>
        /// <param name="entries">key → 新译文。</param>
        /// <returns>本次实际生效的覆盖条数（该层累计）。</returns>
        public int SetOverlay(string sourceId, Language language, IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (string.IsNullOrEmpty(sourceId) || language == null || entries == null) return 0;

            var overlay = GetOrAddOverlay(sourceId);
            foreach (var pair in entries)
            {
                overlay.Add(language, pair.Key, pair.Value);
            }

            return overlay.Count;
        }

        /// <summary>撤掉某个来源的全部覆盖；返回是否确实存在该层。</summary>
        public bool ClearOverlay(string sourceId)
        {
            for (var i = 0; i < _overlays.Count; i++)
            {
                if (_overlays[i].SourceId != sourceId) continue;

                _overlays.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>撤掉全部覆盖层。</summary>
        public void ClearAllOverlays() => _overlays.Clear();

        /// <summary>枚举已登记的覆盖层（来源标识与该层条数），供诊断使用。</summary>
        public List<LocalizationOverlay> GetOverlays() => _overlays;

        #endregion
    }
}