using System.Collections.Generic;

namespace Moirai.Atropos.Localization
{
    public interface ILocalizationModule
    {
        /// <summary>
        /// 当前使用的本地化语言
        /// </summary>
        Language CurrentLanguage { get; }

        /// <summary>
        /// 当前语言索引
        /// </summary>
        int CurrentLanguageIndex { get; }

        public delegate void OnLanguageChangedDelegate(Language language);
        /// <summary>
        /// 当语言改变时调用
        /// </summary>
        public event OnLanguageChangedDelegate OnLanguageChanged;

        /// <summary>
        /// 初始化语言配置。设置当前使用的语言，如果不设置，则默认使用操作系统语言
        /// </summary>
        /// <remarks>不再 Init 初始化，单独调用是因为 依赖 <see cref="Resource.IResourceModule"/> 模块</remarks>
        void InitLanguageSettings();
        
        /// <summary>
        /// 获取当前使用的语言
        /// </summary>
        /// <param name="onlySupported">是否只获取支持的语言，<c>false</c>表示仅根据设置获取语言，不关心本地化是否支持</param>
        /// <param name="settingSource">该语言设置自</param>
        /// <returns></returns>
        Language GetCurrentLanguage(bool onlySupported, ref string settingSource);

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">例如：<see cref="Language.ChineseSimplified"/></param>
        /// <param name="logSource">是否打印设置来源</param>
        void ChangeLanguage(Language language, bool logSource = false);

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">要切换的语言Name或Code</param>
        /// <remarks>不区分大小写。例如简体中文 => "ChineseSimplified" "zh-Hans" "chineseSimplified"均可</remarks>
        void ChangeLanguage(string language);

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="index">要切换已加载的语言索引</param>
        void ChangeLanguage(int index);

        /// <summary>
        /// 激活上一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        string ActivatePreviousLanguage();

        /// <summary>
        /// 激活下一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        string ActivateNextLanguage();

        /// <summary>
        /// 添加本地化器
        /// </summary>
        /// <param name="localizer"></param>
        void AddLocalizer(LocalizerBase localizer);
        
        /// <summary>
        /// 移除本地化器
        /// </summary>
        /// <param name="localizer"></param>
        void RemoveLocalizer(LocalizerBase localizer);

        /// <summary>
        /// 检查当前数据库是否有指定的文本 ID。
        /// </summary>
        /// <param name="textId">文本 ID</param>
        /// <returns></returns>
        bool Has(string textId);
        
        /// <summary>
        /// 根据文本 ID 获取本地化字符串。
        /// </summary>
        /// <param name="textId">文本 ID</param>
        /// <param name="p">Format</param>
        /// <returns>本地化文本</returns>
        string GetTextFromId(string textId, params object[] p);

        /// <summary>
        /// 根据文本 ID 和指定语言获取本地化字符串。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="language">要获取的语言</param>
        /// <param name="p">Format</param>
        /// <returns>本地化文本</returns>
        string GetTextFromIdLanguage(string id, Language language, params object[] p);

        /// <summary>
        /// 获取包含指定 ID 的所有语言的字符串字典。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <returns>包含本地化字符串的字典</returns>
        Dictionary<string, string> GetDictionaryFromId(string id);

        /// <summary>
        /// 获取所有多语言索引
        /// </summary>
        /// <returns></returns>
        List<string> GetAllIds();
    }
}