using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化处理器抽象基类（策略模式抽象策略）。
    /// <para>承载语言管理、语言切换、文本查询与本地化器注册等运行时逻辑。</para>
    /// <para>多语言数据在首次访问时一次性全量加载，各语言列随词条常驻内存；
    /// 按语言拆分懒加载为后续优化项，词条量增大后再实施。</para>
    /// </summary>
    [Serializable]
    public abstract class LocalizationServiceHandler : FrameworkHandler
    {
        [NonSerialized] private Language _currentLanguage;
        // 当前本地化语言设置来自
        [NonSerialized] private string _settingSource;
        // 本地化数据是否已加载（懒式初始化标记）
        [NonSerialized] private bool _dataLoaded;
        // 数据加载失败日志只打一次（数据未就绪时每次查询都会重试加载，避免刷屏）
        [NonSerialized] private bool _hasLoggedLoadError;
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
                if (_currentLanguage == null || LanguageList.Count == 0) return -1;
                return LanguageList.FindIndex(s => s == _currentLanguage);
            }
        }

        // 本地化器列表
        [NonSerialized] private readonly List<LocalizerBase> _localizers = new List<LocalizerBase>();

        /// <summary>已加载的语言列表</summary>
        protected List<Language> LanguageList { get; private set; } = new List<Language>();

        /// <summary>本地化字符串字典</summary>
        protected Dictionary<string, List<string>> LocalizedStrings { get; private set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// 当语言改变时调用
        /// </summary>
        public event Action<Language> OnLanguageChanged;

        protected override void OnShutdown()
        {
            _currentLanguage = null;
            _settingSource = string.Empty;
            _dataLoaded = false;
            _hasLoggedLoadError = false;
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
            // 首次自动解析不持久化设置，避免把系统语言固化进存档
            ChangeLanguage(CurrentLanguage, true, false);
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
                    return;
                }
            }

            LogUtility.Info("Load Localized Text Success!");
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
            EnsureLocalizedStringsLoaded();

            if (LanguageList.Count == 0)
            {
                LogUtility.Error("No language available!");
                return;
            }

            if (_currentLanguage == language) return;

            var languageIndex = GetLanguageIndex(language);
            if (languageIndex == -1) return;

            _currentLanguage = LanguageList[languageIndex];
            OnLanguageChanged?.Invoke(_currentLanguage);

            // 重新注入所有注入器的字符串（快照遍历 + 异常隔离，单个本地化器失败不影响其余，也防注入期间销毁导致的集合变更）
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

            if (persist) SettingUtility.SetString(GameConstant.Setting.LANGUAGE, _currentLanguage.Code);
            LogUtility.Info($"Change the language: {_currentLanguage}{(logSource ? $"(by {_settingSource})" : "")}");
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
        /// 获取语言索引
        /// </summary>
        private int GetLanguageIndex(Language language)
        {
            EnsureLocalizedStringsLoaded();

            // 检查语言列表是否为空
            if (LanguageList == null || LanguageList.Count == 0)
            {
                LogUtility.Error("Language list is empty or null");
                return -1;
            }

            // 语言不存在时降级为 -1（由调用方决定回退方式），不再抛异常破坏静默降级契约
            var i = LanguageList.FindIndex(s => s == language);
            if (i == -1)
            {
                LogUtility.Warning("Language {0} is not available.", language);
            }

            return i;
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
        public bool Has(string id)
        {
            EnsureLocalizedStringsLoaded();
            return !string.IsNullOrEmpty(id) && LocalizedStrings?.ContainsKey(id) == true;
        }

        /// <summary>
        /// 根据文本 ID 获取本地化字符串。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="p">Format</param>
        public string GetTextFromId(string id, params object[] p) => GetTextFromIdLanguage(id, _currentLanguage, p);

        /// <summary>
        /// 根据文本 ID 和指定语言获取本地化字符串。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="language">要获取的语言</param>
        /// <param name="p">Format</param>
        public string GetTextFromIdLanguage(string id, Language language, params object[] p)
        {
            EnsureLocalizedStringsLoaded();

            // 不是多语言直接返回
            if (LocalizedStrings == null || string.IsNullOrEmpty(id) || !LocalizedStrings.TryGetValue(id, out var texts)) return id;

            var languageIndex = GetLanguageIndex(language);
            if (languageIndex == -1 || languageIndex >= texts.Count) return id;

            string text = p is { Length: > 0 }
                ? string.Format(texts[languageIndex], p)
                : texts[languageIndex];

            // 如果该文本没有被翻译，返回 ID
            return string.IsNullOrEmpty(text) ? id : text;
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
