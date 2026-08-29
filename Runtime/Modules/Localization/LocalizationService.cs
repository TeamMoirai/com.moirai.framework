using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化服务外观（Facade）。
    /// <para>统一的静态多语言访问入口，通过替换 <see cref="Handler"/> 即可在不同本地化数据源之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="LocalizationServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(LocalizationServiceHandler))]
    public partial class LocalizationService : ServiceBase
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="LocalizationServiceSettings"/> 创建默认本地化处理器。
        /// </summary>
        /// <returns>默认本地化处理器实例。</returns>
        private static LocalizationServiceHandler CreateDefaultHandler()
        {
            return LocalizationServiceSettings.LocalizationServiceHandler;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        /// <summary>
        /// 当前使用的本地化语言。
        /// </summary>
        public static Language CurrentLanguage => Handler.CurrentLanguage;

        /// <summary>
        /// 当前语言索引。
        /// </summary>
        public static int CurrentLanguageIndex => Handler.CurrentLanguageIndex;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化本地化服务。由容器在构建期调用。
        /// <para>确保 <c>LocalizationService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载），
        /// 并订阅处理器语言变更事件用于静态事件转发。</para>
        /// </summary>
        public override void OnInit()
        {
            // 确保 Handler 已初始化
            _ = Handler;

            Handler.OnLanguageChanged += RaiseLanguageChanged;
        }

        /// <summary>
        /// 关闭本地化服务。由容器在关闭期调用。
        /// </summary>
        public override void Shutdown()
        {
            if (s_Handler != null) s_Handler.OnLanguageChanged -= RaiseLanguageChanged;
            s_Handler = null;
            s_OnLanguageChanged = null;
        }

        #endregion

        #region 事件 [EVENTS]

        private static event Action<Language> s_OnLanguageChanged;

        /// <summary>
        /// 当语言改变时调用。
        /// </summary>
        public static event Action<Language> OnLanguageChanged
        {
            add => s_OnLanguageChanged += value;
            remove => s_OnLanguageChanged -= value;
        }

        private static void RaiseLanguageChanged(Language language) => s_OnLanguageChanged?.Invoke(language);

        #endregion

        #region 语言管理 [LANGUAGE MANAGEMENT]

        /// <summary>
        /// 获取当前使用的语言。
        /// </summary>
        /// <param name="onlySupported">是否只获取支持的语言，<c>false</c>表示仅根据设置获取语言，不关心本地化是否支持</param>
        /// <param name="settingSource">该语言设置自</param>
        public static Language GetCurrentLanguage(bool onlySupported, ref string settingSource)
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

            return ToLanguage(language, onlySupported);
        }

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">例如：<see cref="Language.ChineseSimplified"/></param>
        /// <param name="logSource">是否打印设置来源</param>
        public static void ChangeLanguage(Language language, bool logSource = false) =>
            Handler.ChangeLanguage(language, logSource);

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">要切换的语言Name或Code</param>
        public static void ChangeLanguage(string language) => Handler.ChangeLanguage(language);

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="index">要切换已加载的语言索引</param>
        public static void ChangeLanguage(int index) => Handler.ChangeLanguage(index);

        /// <summary>
        /// 激活上一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        public static string ActivatePreviousLanguage() => Handler.ActivatePreviousLanguage();

        /// <summary>
        /// 激活下一个语言。
        /// </summary>
        /// <returns>激活的语言名称</returns>
        public static string ActivateNextLanguage() => Handler.ActivateNextLanguage();

        #endregion

        #region 文本查询 [TEXT QUERIES]

        /// <summary>
        /// 检查当前数据库是否有指定的文本 ID。
        /// </summary>
        public static bool Has(string id) => Handler.Has(id);

        /// <summary>
        /// 根据文本 ID 获取本地化字符串。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="p">Format</param>
        public static string GetTextFromId(string id, params object[] p) =>
            Handler.GetTextFromId(id, p);

        /// <summary>
        /// 根据文本 ID 和指定语言获取本地化字符串。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="language">要获取的语言</param>
        /// <param name="p">Format</param>
        public static string GetTextFromIdLanguage(string id, Language language, params object[] p) =>
            Handler.GetTextFromIdLanguage(id, language, p);

        /// <summary>
        /// 获取包含指定 ID 的所有语言的字符串字典。
        /// </summary>
        public static Dictionary<string, string> GetDictionaryFromId(string id) =>
            Handler.GetDictionaryFromId(id);

        /// <summary>
        /// 获取所有多语言索引。
        /// </summary>
        public static List<string> GetAllIds() => Handler.GetAllIds();

        #endregion

        #region 本地化器 [LOCALIZERS]

        /// <summary>
        /// 添加本地化器。
        /// </summary>
        public static void AddLocalizer(LocalizerBase localizer) => Handler.AddLocalizer(localizer);

        /// <summary>
        /// 移除本地化器。
        /// </summary>
        public static void RemoveLocalizer(LocalizerBase localizer) => Handler.RemoveLocalizer(localizer);

        #endregion
    }
}
