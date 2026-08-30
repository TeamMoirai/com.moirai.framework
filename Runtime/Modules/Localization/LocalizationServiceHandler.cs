using System;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化服务配置抽象基类（纯数据，无行为无生命周期）。
    /// <para>以 <see cref="UnityEngine.SerializeReference"/> 存于 <see cref="LocalizationServiceSettings"/> 资产；
    /// 经 <see cref="CreateHandler"/> 工厂创建绑定的数据源处理器实例，处理器不再被序列化。</para>
    /// </summary>
    [Serializable]
    public abstract class LocalizationServiceHandlerConfig
    {
        /// <summary>
        /// 创建配置绑定的本地化数据源处理器实例。
        /// </summary>
        /// <returns>新的本地化处理器实例。</returns>
        public abstract LocalizationServiceHandler CreateHandler();
    }

    /// <summary>
    /// 本地化处理器抽象基类（策略模式抽象策略）。
    /// <para>公共契约由本类承载（语言管理等具体实现），数据源钩子 <c>LoadLocalizedData</c> 为 protected internal；配置数据由 <see cref="LocalizationServiceHandlerConfig"/> 系列纯数据类承载，由 <see cref="LocalizationServiceHandlerConfig.CreateHandler"/> 工厂在运行期创建。</para>
    /// <para>处理器实例为普通运行时类，不参与序列化（由 <see cref="LocalizationServiceHandlerConfig.CreateHandler"/> 工厂创建），
    /// 运行时字段无快照污染风险。</para>
    /// </summary>
    public abstract class LocalizationServiceHandler : FrameworkHandler
    {
        private Language _currentLanguage;
        // 当前本地化语言设置来自
        private string _settingSource;
        // 本地化数据是否已加载（懒式初始化标记）
        private bool _dataLoaded;
        /// <summary>
        /// 当前使用的本地化语言
        /// </summary>
        public Language CurrentLanguage => _currentLanguage ?? LocalizationService.GetCurrentLanguage(true, ref _settingSource);

        /// <summary>
        /// 当前语言索引
        /// </summary>
        public int CurrentLanguageIndex => GetLanguageIndex(_currentLanguage);

        // 本地化器列表
        private readonly List<LocalizerBase> _localizers = new List<LocalizerBase>();

        /// <summary>已加载的语言列表</summary>
        protected List<Language> LanguageList { get; private set; } = new List<Language>();

        /// <summary>本地化字符串字典</summary>
        protected Dictionary<string, List<string>> LocalizedStrings { get; private set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// 当语言改变时调用
        /// </summary>
        public event Action<Language> OnLanguageChanged;

        /// <summary>
        /// 加载本地化数据源。
        /// </summary>
        /// <returns>语言列表与本地化字符串字典。</returns>
        protected internal abstract (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData();

        protected override void OnShutdown()
        {
            _currentLanguage = null;
            _settingSource = string.Empty;
            _dataLoaded = false;
            _localizers.Clear();
        }

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
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">例如：<see cref="Language.ChineseSimplified"/></param>
        /// <param name="logSource">是否打印设置来源</param>
        public void ChangeLanguage(Language language, bool logSource = false)
        {
            EnsureLocalizedStringsLoaded();

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
        public void ChangeLanguage(int index)
        {
            EnsureLocalizedStringsLoaded();
            ChangeLanguage(LanguageList[index]);
        }

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
            EnsureLocalizedStringsLoaded();

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
        public bool Has(string id)
        {
            EnsureLocalizedStringsLoaded();
            return LocalizedStrings.ContainsKey(id);
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
            EnsureLocalizedStringsLoaded();

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
        public List<string> GetAllIds()
        {
            EnsureLocalizedStringsLoaded();
            return LocalizedStrings.Keys.ToList();
        }
    }
}
