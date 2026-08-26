using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化处理器抽象基类（策略模式抽象策略）。
    /// <para>承载语言管理、语言切换、文本查询与本地化器注册等运行时逻辑；
    /// 本地化数据源（语言列表与字符串字典）由子类通过 <see cref="LoadLocalizedData"/> 提供。</para>
    /// </summary>
    [Serializable]
    public abstract class LocalizationHandler : FrameworkHandler
    {
        // 本地化器列表
        private readonly List<LocalizerBase> _localizers = new List<LocalizerBase>();

        /// <summary>已加载的语言列表</summary>
        protected List<Language> LanguageList { get; private set; } = new List<Language>();

        /// <summary>本地化字符串字典</summary>
        protected Dictionary<string, List<string>> LocalizedStrings { get; private set; } = new Dictionary<string, List<string>>();

        // 当前本地化语言
        private Language _currentLanguage;
        // 当前本地化语言设置来自
        private string _settingSource;

        /// <summary>
        /// 当语言改变时调用
        /// </summary>
        public event Action<Language> OnLanguageChanged;

        /// <summary>
        /// 当前使用的本地化语言
        /// </summary>
        public Language CurrentLanguage => _currentLanguage ?? GetCurrentLanguage(true, ref _settingSource);

        /// <summary>
        /// 当前语言索引
        /// </summary>
        public int CurrentLanguageIndex => GetLanguageIndex(_currentLanguage);

        /// <summary>
        /// 加载本地化数据源。
        /// </summary>
        /// <returns>语言列表与本地化字符串字典。</returns>
        protected internal abstract (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData();

        /// <summary>
        /// 初始化语言配置。设置当前使用的语言，如果不设置，则默认使用操作系统语言。
        /// </summary>
        /// <remarks>单独调用是因为依赖配置表数据加载时机。</remarks>
        public void InitLanguageSettings()
        {
            if (LocalizedStrings.Count == 0)
            {
                LocalizedStrings.Clear();
                LoadLocalizedStrings();
            }

            ChangeLanguage(CurrentLanguage, true);
        }

        /// <summary>
        /// 从数据源加载本地化字符串到内存。
        /// </summary>
        private void LoadLocalizedStrings()
        {
            (LanguageList, LocalizedStrings) = LoadLocalizedData();

            if (LanguageList.Count != 0 && LocalizedStrings != null)
            {
                LogUtility.Info("Load Localized Text Success!");
            }
            else
            {
                LogUtility.Error("Failed to load localized text, generate config first!");
            }
        }

        /// <summary>
        /// 获取当前使用的语言
        /// </summary>
        /// <param name="onlySupported">是否只获取支持的语言，<c>false</c>表示仅根据设置获取语言，不关心本地化是否支持</param>
        /// <param name="settingSource">该语言设置自</param>
        public Language GetCurrentLanguage(bool onlySupported, ref string settingSource)
        {
            // 获取启动命令中的设置
            string language = CommandLineUtility.GetForceLanguage();
            if (!string.IsNullOrEmpty(language))
            {
                settingSource = "CommandLine";
            }
            else
            {
#if UNITY_EDITOR
                // 如果处于编辑器模拟模式下，使用编辑器设置的语言
                if (GameAppSettings.EditorLanguage != Language.Unspecified.Name)
                {
                    language = GameAppSettings.EditorLanguage;
                    settingSource = "EditorSetting";
                }
                else
#endif
                // 如果已设置语言，则使用设置的语言
                if (SettingUtility.HasSetting(GameConstant.Setting.LANGUAGE))
                {
                    language = SettingUtility.GetString(GameConstant.Setting.LANGUAGE);
                    settingSource = "SavedSetting";
                }
                // 否则，使用系统语言
                else
                {
                    SystemLanguage systemLanguage = Application.systemLanguage;
                    // 未区分简繁时，使用简体中文
                    if (systemLanguage == SystemLanguage.Chinese)
                    {
                        systemLanguage = SystemLanguage.ChineseSimplified;
                    }
                    language = ((Language)systemLanguage).Code;
                    settingSource = "SystemLanguage";
                }
            }

            return LocalizationService.ToLanguage(language, onlySupported);
        }

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">例如：<see cref="Language.ChineseSimplified"/></param>
        /// <param name="logSource">是否打印设置来源</param>
        public void ChangeLanguage(Language language, bool logSource = false)
        {
            if (LanguageList.Count == 0)
            {
                LogUtility.Error("No language available!");
                return;
            }

            if (_currentLanguage == language) return;

            _currentLanguage = LanguageList[GetLanguageIndex(language)];
            OnLanguageChanged?.Invoke(_currentLanguage);

            // 重新注入所有注入器的字符串。
            _localizers.ForEach(_ => _.Localize());

            SettingUtility.SetString(GameConstant.Setting.LANGUAGE, _currentLanguage.Code);
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
        public void ChangeLanguage(int index) => ChangeLanguage(LanguageList[index]);

        /// <summary>
        /// 激活上一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        public string ActivatePreviousLanguage()
        {
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
            var nextIndex = (int)Mathf.Repeat(CurrentLanguageIndex + 1, LanguageList.Count);
            ChangeLanguage(LanguageList[nextIndex]);
            return LanguageList[nextIndex].Name;
        }

        /// <summary>
        /// 获取语言索引
        /// </summary>
        private int GetLanguageIndex(Language language)
        {
            // 检查语言列表是否为空
            if (LanguageList == null || !LanguageList.Any())
            {
                LogUtility.Error("Language list is empty or null");
                throw new InvalidOperationException("Language list is empty or null");
            }

            // 进行匹配
            var i = LanguageList.FindIndex(s => s == language);

            // 处理语言不存在的情况
            if (i == -1)
            {
                LogUtility.Error($"Language {language} is not available");
                throw new KeyNotFoundException($"Language {language} is not available");
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
        public bool Has(string id) => LocalizedStrings.ContainsKey(id);

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
            // 不是多语言直接返回
            if (!LocalizedStrings.ContainsKey(id)) return id;

            var languageIndex = GetLanguageIndex(language);
            string text = p is { Length: > 0 }
                ? string.Format(LocalizedStrings[id][languageIndex], p)
                : LocalizedStrings[id][languageIndex];

            // 如果该文本没有被翻译，返回 ID
            return string.IsNullOrEmpty(text) ? id : text;
        }

        /// <summary>
        /// 获取包含指定 ID 的所有语言的字符串字典。
        /// </summary>
        public Dictionary<string, string> GetDictionaryFromId(string id)
        {
            var dict = new Dictionary<string, string>();
            if (!LocalizedStrings.ContainsKey(id)) return dict;

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
        public List<string> GetAllIds() => LocalizedStrings.Keys.ToList();
    }
}
