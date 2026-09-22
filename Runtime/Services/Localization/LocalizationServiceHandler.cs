using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化处理器抽象基类（策略模式抽象策略）。
    /// <para>承载语言管理、语言切换、缺译回退、文本查询与本地化器注册等运行时逻辑。</para>
    /// <para>多语言数据在首次访问时一次性全量加载，各语言列随词条常驻内存；
    /// 按语言拆分懒加载为后续优化项，词条量增大后再实施
    /// （常驻规模可经 <see cref="TotalTextLength"/> 在调试面板上量化判断）。</para>
    /// </summary>
    [Serializable]
    public abstract class LocalizationServiceHandler : FrameworkHandler
    {
        // 缺译回退顺序，用语言 Code 而非 Language 配置：Language 无无参构造，
        // Unity 序列化器无法还原其数组元素。留空即关闭回退（缺译直接返回 key）。
        [Tooltip("缺译回退顺序，填语言 Code（如 en、zh-Hans）。当前语言缺译时按此顺序取译文；留空表示缺译直接返回 key。")]
        [SerializeField] private string[] m_FallbackLanguageCodes = { "en" };

        [NonSerialized] private Language _currentLanguage;
        // 当前本地化语言设置来自
        [NonSerialized] private string _settingSource;
        // 本地化数据是否已加载（懒式初始化标记）
        [NonSerialized] private bool _dataLoaded;
        // 数据加载失败日志只打一次（数据未就绪时每次查询都会重试加载，避免刷屏）
        [NonSerialized] private bool _hasLoggedLoadError;
        // 格式化失败日志只打一次（占位符与参数不匹配属表内缺陷，逐条刷屏会淹没日志）
        [NonSerialized] private bool _hasLoggedFormatError;
        // 当前语言在 LanguageList 中的下标：查询热路径用，省去每次线性扫语言表
        [NonSerialized] private int _currentLanguageIndex = -1;
        // 切换中标记：本地化器注入回调里再切语言会打乱快照与事件顺序，直接拦下
        [NonSerialized] private bool _isSwitching;
        // 回退链解析后的语言与其在 LanguageList 中的下标
        [NonSerialized] private Language[] _fallbackChain = Array.Empty<Language>();
        [NonSerialized] private int[] _fallbackIndices = Array.Empty<int>();
        // 不存在的语言只在切换时警告一次
        [NonSerialized] private HashSet<Language> _warnedUnavailableLanguages;
        // 所有语言列的译文总字符数（加载期统计一次，供常驻规模估算）
        [NonSerialized] private int _totalTextLength;

        /// <summary>
        /// 当前使用的本地化语言
        /// </summary>
        public Language CurrentLanguage => _currentLanguage ?? LocalizationService.GetCurrentLanguage(true, ref _settingSource);

        /// <summary>
        /// 当前语言索引（数据未就绪或语言未解析时为 -1）
        /// </summary>
        public int CurrentLanguageIndex
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return _currentLanguageIndex;
            }
        }

        /// <summary>
        /// 缺译回退链（不含当前语言，按 <see cref="m_FallbackLanguageCodes"/> 配置顺序）。
        /// <para>访问会触发数据加载；配置了不可用的语言时按序剔除。</para>
        /// </summary>
        public IReadOnlyList<Language> FallbackChain
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return _fallbackChain;
            }
        }

        /// <summary>
        /// 缺译回退链的语言 Code 配置（按回退顺序）。
        /// </summary>
        /// <remarks>用 Code 字符串而非 <see cref="Language"/>：后者无无参构造，Unity 序列化器还原不了数组元素。
        /// 赋值时若数据已加载会立即重解析回退链。</remarks>
        public string[] FallbackLanguageCodes
        {
            get => m_FallbackLanguageCodes;
            set
            {
                m_FallbackLanguageCodes = value;
                if (_dataLoaded) ResolveFallbackChain();
            }
        }

        /// <summary>已加载的词条数（数据未就绪时为 0）</summary>
        public int EntryCount
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return LocalizedStrings?.Count ?? 0;
            }
        }

        /// <summary>已加载的语言数（数据未就绪时为 0）</summary>
        public int LanguageCount
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return LanguageList?.Count ?? 0;
            }
        }

        /// <summary>
        /// 全部语言列的译文总字符数。
        /// </summary>
        /// <remarks>UTF-16 下每字符 2 字节，是常驻译文的<b>下限</b>估算（不含字符串对象头与字典开销），
        /// 用于判断是否到了必须按语言拆包加载的量级。</remarks>
        public int TotalTextLength
        {
            get
            {
                EnsureLocalizedStringsLoaded();
                return _totalTextLength;
            }
        }

        // 本地化器列表
        [NonSerialized] private readonly List<LocalizerBase> _localizers = new List<LocalizerBase>();

        /// <summary>已加载的语言列表</summary>
        protected List<Language> LanguageList { get; private set; } = new List<Language>();

        /// <summary>本地化字符串字典</summary>
        protected Dictionary<string, List<string>> LocalizedStrings { get; private set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// 当语言改变时调用。
        /// </summary>
        /// <remarks>在全部本地化器重注入<em>之后</em>触发，回调内查询文本即已是新语言。</remarks>
        public event Action<Language> OnLanguageChanged;

        protected override void OnShutdown()
        {
            _currentLanguage = null;
            _settingSource = string.Empty;
            _dataLoaded = false;
            _hasLoggedLoadError = false;
            _hasLoggedFormatError = false;
            _currentLanguageIndex = -1;
            _isSwitching = false;
            _fallbackChain = Array.Empty<Language>();
            _fallbackIndices = Array.Empty<int>();
            _warnedUnavailableLanguages = null;
            _totalTextLength = 0;
            _localizers.Clear();
        }

        /// <summary>
        /// 加载本地化数据源。
        /// </summary>
        /// <returns>语言列表与本地化字符串字典。</returns>
        protected abstract (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData();

        /// <summary>
        /// 懒式加载本地化数据源并解析当前语言。
        /// <para>数据加载依赖资源服务（配置表），服务注册期资源尚未就绪；
        /// 首次访问多语言 API 时资源必然已加载完成，故推迟到调用点执行。</para>
        /// <para>解析结果为空（语言列表为空）视为数据未就绪，不置成功标记，下次访问自动重试。</para>
        /// </summary>
        private void EnsureLocalizedStringsLoaded()
        {
            if (_dataLoaded) return;

            LoadLocalizedStrings();
            if (LanguageList.Count == 0) return;

            _dataLoaded = true;
            ResolveFallbackChain();
            // 首次自动解析不持久化设置，避免把系统语言固化进存档
            ChangeLanguage(ResolveInitialLanguage(), true, false);
        }

        /// <summary>
        /// 解析首启语言：检测链结果优先，不在发行语言表里时按回退链、再按表头兜底。
        /// <para>检测链给出的语言完全可能没随包发行（中文系统跑只出英日两语的包）。
        /// 早退会让 <see cref="_currentLanguage"/> 停在 null，于是<b>每一条</b>查询都露出 key——
        /// 首启必须落在一个真实存在的语言上。</para>
        /// </summary>
        private Language ResolveInitialLanguage()
        {
            var list = LanguageList;
            var detected = CurrentLanguage;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == detected) return list[i];
            }

            var fallbackIndices = _fallbackIndices;
            if (fallbackIndices.Length > 0) return list[fallbackIndices[0]];

            return list[0];
        }

        /// <summary>
        /// 从数据源加载本地化字符串到内存。
        /// </summary>
        private void LoadLocalizedStrings()
        {
            (LanguageList, LocalizedStrings) = LoadLocalizedData();

            if (LanguageList.Count == 0 || LocalizedStrings == null)
            {
                // 数据未就绪时每次查询都会重试进入此处，错误日志只打一次
                if (!_hasLoggedLoadError)
                {
                    _hasLoggedLoadError = true;
                    LogUtility.Error("Failed to load localized text, generate config first!");
                }
                return;
            }

            // 校验词条的语言列数与语言列表一致：下标错位会表现为"显示错误语言"而非报错，必须在加载期拦下
            // 失败时清空数据，避免半损坏状态被 Ensure 标记为已加载
            var totalTextLength = 0;
            foreach (var pair in LocalizedStrings)
            {
                if (pair.Value.Count != LanguageList.Count)
                {
                    if (!_hasLoggedLoadError)
                    {
                        _hasLoggedLoadError = true;
                        LogUtility.Error("Localized strings '{0}' has {1} language columns, but {2} languages are registered.",
                            pair.Key, pair.Value.Count, LanguageList.Count);
                    }

                    LanguageList = new List<Language>();
                    LocalizedStrings = new Dictionary<string, List<string>>();
                    _totalTextLength = 0;
                    return;
                }

                for (var i = 0; i < pair.Value.Count; i++)
                {
                    totalTextLength += pair.Value[i]?.Length ?? 0;
                }
            }

            _totalTextLength = totalTextLength;
            LogUtility.Info("Load Localized Text Success!");
        }

        /// <summary>
        /// 解析回退链配置：把语言 Code 换成 <see cref="LanguageList"/> 中真实存在的语言与其下标。
        /// <para>识别不了的语言 Code 会被剔除并警告——静默落到默认语言会让配置错误一路带到上线。</para>
        /// </summary>
        private void ResolveFallbackChain()
        {
            _fallbackChain = Array.Empty<Language>();
            _fallbackIndices = Array.Empty<int>();

            if (m_FallbackLanguageCodes == null || m_FallbackLanguageCodes.Length == 0) return;

            var chain = new List<Language>(m_FallbackLanguageCodes.Length);
            var indices = new List<int>(m_FallbackLanguageCodes.Length);
            foreach (var code in m_FallbackLanguageCodes)
            {
                if (string.IsNullOrEmpty(code)) continue;

                if (!LocalizationService.TryGetBuiltInLanguage(code, out var language))
                {
                    LogUtility.Warning("Fallback language '{0}' is not a built-in language Name/Code, skipped.", code);
                    continue;
                }

                var index = LanguageList.FindIndex(s => s == language);
                if (index == -1)
                {
                    LogUtility.Warning("Fallback language {0} is not present in the localized data, skipped.", language);
                    continue;
                }

                if (chain.Contains(language)) continue;

                chain.Add(language);
                indices.Add(index);
            }

            _fallbackChain = chain.ToArray();
            _fallbackIndices = indices.ToArray();
        }

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">例如：<see cref="Language.ChineseSimplified"/></param>
        /// <param name="logSource">是否打印设置来源</param>
        public void ChangeLanguage(Language language, bool logSource = false) => ChangeLanguage(language, logSource, true);

        /// <summary>
        /// 更改当前语言的内部实现。
        /// </summary>
        /// <param name="persist">是否持久化设置；仅用户显式切换为 true，首次自动解析不写入，避免固化系统语言。</param>
        private void ChangeLanguage(Language language, bool logSource, bool persist)
        {
            if (_isSwitching)
            {
                // 注入回调内嵌套切换会让快照与事件顺序失效，且极易造成无限递归
                LogUtility.Error("ChangeLanguage({0}) is ignored: another language switch is already in progress.", language);
                return;
            }

            EnsureLocalizedStringsLoaded();

            if (LanguageList.Count == 0)
            {
                LogUtility.Error("No language available!");
                return;
            }

            if (_currentLanguage == language) return;

            var languageIndex = IndexOfLanguage(language);
            if (languageIndex == -1)
            {
                WarnLanguageUnavailable(language);
                return;
            }

            _isSwitching = true;
            try
            {
                _currentLanguage = LanguageList[languageIndex];
                _currentLanguageIndex = languageIndex;

                // 先重注入再抛事件：订阅者在 OnLanguageChanged 回调里取文本必须已拿到新语言。
                // 快照遍历 + 异常隔离，单个本地化器失败不影响其余，也防注入期间销毁导致的集合变更
                var snapshot = _localizers.ToArray();
                foreach (var localizer in snapshot)
                {
                    if (localizer == null) continue;

                    try
                    {
                        localizer.Localize();
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error(ex);
                    }
                }

                OnLanguageChanged?.Invoke(_currentLanguage);

                if (persist) SettingUtility.SetString(GameConstant.Setting.LANGUAGE, _currentLanguage.Code);
                LogUtility.Info($"Change the language: {_currentLanguage}{(logSource ? $"(by {_settingSource})" : "")}");
            }
            finally
            {
                _isSwitching = false;
            }
        }

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">要切换的语言Name或Code</param>
        /// <remarks>不区分大小写。例如简体中文 => "ChineseSimplified" "zh-Hans" "chineseSimplified"均可</remarks>
        public void ChangeLanguage(string language) => ChangeLanguage(LocalizationService.ToLanguage(language, true));

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="index">要切换已加载的语言索引</param>
        public void ChangeLanguage(int index)
        {
            EnsureLocalizedStringsLoaded();
            if (index < 0 || index >= LanguageList.Count)
            {
                LogUtility.Error("Language index {0} out of range [0, {1}).", index, LanguageList.Count);
                return;
            }

            ChangeLanguage(LanguageList[index]);
        }

        /// <summary>
        /// 激活上一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        public string ActivatePreviousLanguage()
        {
            EnsureLocalizedStringsLoaded();
            if (LanguageList.Count == 0)
            {
                LogUtility.Error("No language available!");
                return null;
            }

            var prevIndex = (int)Mathf.Repeat(CurrentLanguageIndex - 1, LanguageList.Count);
            ChangeLanguage(LanguageList[prevIndex]);
            return LanguageList[prevIndex].Name;
        }

        /// <summary>
        /// 激活下一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        public string ActivateNextLanguage()
        {
            EnsureLocalizedStringsLoaded();
            if (LanguageList.Count == 0)
            {
                LogUtility.Error("No language available!");
                return null;
            }

            var nextIndex = (int)Mathf.Repeat(CurrentLanguageIndex + 1, LanguageList.Count);
            ChangeLanguage(LanguageList[nextIndex]);
            return LanguageList[nextIndex].Name;
        }

        /// <summary>
        /// 取语言在 <see cref="LanguageList"/> 中的下标，未收录时为 -1。
        /// </summary>
        /// <remarks>当前语言走缓存下标，其余语言（显式跨语言查询）才线性扫表。</remarks>
        private int IndexOfLanguage(Language language)
        {
            if (language == null) return -1;
            if (_currentLanguage != null && language == _currentLanguage) return _currentLanguageIndex;

            var list = LanguageList;
            if (list == null) return -1;

            for (var i = 0; i < list.Count; i++)
            {
                if (language == list[i]) return i;
            }

            return -1;
        }

        private void WarnLanguageUnavailable(Language language)
        {
            _warnedUnavailableLanguages ??= new HashSet<Language>();
            if (!_warnedUnavailableLanguages.Add(language)) return;

            LogUtility.Warning("Language {0} is not available.", language);
        }

        /// <summary>
        /// 添加本地化器
        /// </summary>
        public void AddLocalizer(LocalizerBase localizer) => _localizers.Add(localizer);

        /// <summary>
        /// 移除本地化器
        /// </summary>
        public void RemoveLocalizer(LocalizerBase localizer) => _localizers.Remove(localizer);

        /// <summary>
        /// 检查当前数据库是否有指定的文本 ID。
        /// </summary>
        /// <remarks>只断言词条存在，不代表当前语言已有译文（缺译时仍会命中回退链或返回 ID）。</remarks>
        public bool Has(string id)
        {
            EnsureLocalizedStringsLoaded();
            return !string.IsNullOrEmpty(id) && LocalizedStrings?.ContainsKey(id) == true;
        }

        /// <summary>
        /// 根据文本 ID 获取当前语言的本地化字符串。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="p">Format</param>
        public string GetTextFromId(string id, params object[] p)
        {
            EnsureLocalizedStringsLoaded();
            return ResolveText(_currentLanguage, id, p);
        }

        /// <summary>
        /// 根据文本 ID 和指定语言获取本地化字符串。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="language">要获取的语言；<c>null</c> 表示当前语言</param>
        /// <param name="p">Format</param>
        public string GetTextFromIdLanguage(string id, Language language, params object[] p)
        {
            EnsureLocalizedStringsLoaded();
            return ResolveText(language ?? _currentLanguage, id, p);
        }

        /// <summary>
        /// 按「指定语言 → 回退链 → ID 原文」解析译文。调用方须已确保数据加载完成。
        /// </summary>
        private string ResolveText(Language language, string id, object[] args)
        {
            var strings = LocalizedStrings;
            // 词条不存在时无列可回退，直接露出 ID
            if (strings == null || string.IsNullOrEmpty(id) || !strings.TryGetValue(id, out var texts)) return id;

            var languageIndex = IndexOfLanguage(language);
            var text = SelectText(texts, languageIndex);
            if (text != null) return Format(id, text, args);

            var fallbackIndices = _fallbackIndices;
            for (var i = 0; i < fallbackIndices.Length; i++)
            {
                text = SelectText(texts, fallbackIndices[i]);
                if (text != null) return Format(id, text, args);
            }

            // 全链缺译：返回 ID 而非空白，保证 UI 上看得见键名以便定位
            return id;
        }

        /// <summary>
        /// 取指定下标的译文；下标越界或译文为空/仅空白时视为缺译，返回 <c>null</c>。
        /// </summary>
        private static string SelectText(List<string> texts, int index)
        {
            if ((uint)index >= (uint)texts.Count) return null;

            var text = texts[index];
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>
        /// 套用格式化参数；表内占位符与参数不匹配时退化为未格式化原文。
        /// </summary>
        private string Format(string id, string text, object[] args)
        {
            if (args is not { Length: > 0 }) return text;

            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                // 一条文案写坏占位符不该把整块界面的查询抛出去，日志同样只打一次
                if (!_hasLoggedFormatError)
                {
                    _hasLoggedFormatError = true;
                    LogUtility.Error("Localized text '{0}' has invalid placeholders for {1} argument(s): \"{2}\"",
                        id, args.Length, text);
                }

                return text;
            }
        }

        /// <summary>
        /// 获取包含指定 ID 的所有语言的字符串字典。
        /// </summary>
        public Dictionary<string, string> GetDictionaryFromId(string id)
        {
            EnsureLocalizedStringsLoaded();

            var dict = new Dictionary<string, string>();
            if (LocalizedStrings == null || string.IsNullOrEmpty(id) || !LocalizedStrings.ContainsKey(id)) return dict;

            foreach (var language in LanguageList)
            {
                var text = GetTextFromIdLanguage(id, language);
                dict.Add(language.Name, text);
            }

            return dict;
        }

        /// <summary>
        /// 获取所有多语言索引
        /// </summary>
        public List<string> GetAllIds()
        {
            EnsureLocalizedStringsLoaded();
            return LocalizedStrings?.Keys.ToList() ?? new List<string>();
        }
    }
}
