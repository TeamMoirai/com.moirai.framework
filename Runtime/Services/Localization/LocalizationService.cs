using System;
using System.Collections.Generic;
using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Debugger;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化服务外观（Facade）。
    /// <para>统一的静态多语言访问入口，通过替换 <see cref="Handler"/> 即可在不同本地化数据源之间零成本切换。</para>
    /// <para>未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="LocalizationServiceSettings"/> 解析；settings 未配置则回退 <see cref="CreateDefaultHandler"/>。</para>
    /// <para>降级契约：全部外观 API 经 <c>s_Handler?.</c> 静默降级（未注册/未初始化时返回安全默认值），与全框架统一。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [AutoRegisterService]
    [ServiceDependency(typeof(DebuggerService), typeof(ConfigTableService))]
    [HandlerHost(typeof(LocalizationServiceHandler))]
    public partial class LocalizationService : ServiceBase
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认本地化处理器（settings 未配置时的代码兜底）。
        /// </summary>
        /// <returns>默认本地化处理器实例。</returns>
        internal static LocalizationServiceHandler CreateDefaultHandler() => new ConfigTableLocalizationHandler();

        /// <summary>
        /// 从 <see cref="LocalizationServiceSettings"/> 解析本地化处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——懒加载主路径（settings 已配置时 <see cref="CreateDefaultHandler"/> 被短路）首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static LocalizationServiceHandler GetHandlerFromSettings()
        {
            GameServices.EnsureRegistered<LocalizationService>();
            return LocalizationServiceSettings.LocalizationServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化本地化服务。由容器在构建期调用。
        /// <para>确保 <c>LocalizationService.Handler</c> 已赋值（触发 <c>Handler</c> 懒加载），
        /// 订阅处理器语言变更事件用于静态事件转发，
        /// 并向游戏内调试器注册调试面板（依赖组合根先注册 <see cref="DebuggerService"/>——外观未就绪时静默跳过）。</para>
        /// <para>依赖 <see cref="ConfigTableService"/>：默认数据源（<see cref="ConfigTableLocalizationHandler"/>）
        /// 从配置表读取语言列表与字符串字典，处理器懒加载即可能触发首次读表——该依赖必须显式声明，
        /// 否则初始化序会退化为注册序（历史故障：本地化先于配置表/资源服务初始化，首次读表失败）。</para>
        /// </summary>
        public override void OnInit()
        {
            Handler.OnLanguageChanged += OnLanguageChanged;
            DebuggerService.RegisterDebuggerWindow("Profiler/Localization", new LocalizationInformationWindow());
        }

        /// <summary>
        /// 关闭本地化服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();

            if (handler != null) handler.OnLanguageChanged -= OnLanguageChanged;
            OnLanguageChanged = null;
            ResetOneShotLogs();
        }

        #endregion

        #region 属性 [PROPERTIES]
		
        /// <summary>
        /// 当前使用的本地化语言（未就绪时为 <see cref="Language.Unspecified"/>）。
        /// </summary>
        public static Language CurrentLanguage => s_Handler?.CurrentLanguage ?? Language.Unspecified;

        /// <summary>
        /// 当前语言索引（未就绪时为 -1）。
        /// </summary>
        public static int CurrentLanguageIndex => s_Handler?.CurrentLanguageIndex ?? -1;

        /// <summary>
        /// 缺译回退链（未就绪时为空）。
        /// </summary>
        public static IReadOnlyList<Language> FallbackChain => s_Handler?.FallbackChain ?? Array.Empty<Language>();

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <summary>已加载词条数（未就绪时为 0）。</summary>
        public static int EntryCount => s_Handler?.EntryCount ?? 0;

        /// <summary>已加载语言数（未就绪时为 0）。</summary>
        public static int LoadedLanguageCount => s_Handler?.LanguageCount ?? 0;

        /// <summary>
        /// 全部语言列的译文总字符数——常驻译文的规模下限（未就绪时为 0）。
        /// </summary>
        /// <remarks>UTF-16 每字符 2 字节，不含字符串对象头与字典开销。
        /// 用于判断是否已到必须按语言拆包加载的量级。</remarks>
        public static int TotalTextLength => s_Handler?.TotalTextLength ?? 0;

        #endregion

        #region 事件 [EVENTS]

        /// <summary>
        /// 当语言改变时调用。
        /// </summary>
        public static event Action<Language> OnLanguageChanged;

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
                if (LocalizationServiceSettings.EditorLanguage != Language.Unspecified.Name)
                {
                    language = LocalizationServiceSettings.EditorLanguage;
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
            s_Handler?.ChangeLanguage(language, logSource);

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="language">要切换的语言Name或Code</param>
        public static void ChangeLanguage(string language) => s_Handler?.ChangeLanguage(language);

        /// <summary>
        /// 更改当前语言。
        /// </summary>
        /// <param name="index">要切换已加载的语言索引</param>
        public static void ChangeLanguage(int index) => s_Handler?.ChangeLanguage(index);

        /// <summary>
        /// 激活上一个语言。
        /// </summary>
        /// <returns>激活的语言名称（未就绪时为 null）</returns>
        public static string ActivatePreviousLanguage() => s_Handler?.ActivatePreviousLanguage();

        /// <summary>
        /// 激活下一个语言。
        /// </summary>
        /// <returns>激活的语言名称（未就绪时为 null）</returns>
        public static string ActivateNextLanguage() => s_Handler?.ActivateNextLanguage();

        #endregion

        #region 文本查询 [TEXT QUERIES]

        /// <summary>
        /// 检查当前数据库是否有指定的文本 ID（未就绪时为 false）。
        /// </summary>
        public static bool Has(string id) => s_Handler?.Has(id) ?? false;

        /// <summary>
        /// 根据文本 ID 获取本地化字符串（未就绪时返回 id 原文——保证 UI 可见键名而非空白）。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="p">Format</param>
        public static string GetTextFromId(string id, params object[] p) =>
            s_Handler?.GetTextFromId(id, p) ?? id;

        /// <summary>
        /// 根据文本 ID 和指定语言获取本地化字符串（未就绪时返回 id 原文）。
        /// </summary>
        /// <param name="id">文本 ID</param>
        /// <param name="language">要获取的语言</param>
        /// <param name="p">Format</param>
        public static string GetTextFromIdLanguage(string id, Language language, params object[] p) =>
            s_Handler?.GetTextFromIdLanguage(id, language, p) ?? id;

        /// <summary>
        /// 获取包含指定 ID 的所有语言的字符串字典（未就绪时为 null）。
        /// </summary>
        public static Dictionary<string, string> GetDictionaryFromId(string id) =>
            s_Handler?.GetDictionaryFromId(id);

        /// <summary>
        /// 获取所有多语言索引（未就绪时为 null）。
        /// </summary>
        public static List<string> GetAllIds() => s_Handler?.GetAllIds();

        #endregion

        #region 本地化器 [LOCALIZERS]

        /// <summary>
        /// 添加本地化器。
        /// </summary>
        public static void AddLocalizer(LocalizerBase localizer) => s_Handler?.AddLocalizer(localizer);

        /// <summary>
        /// 移除本地化器。
        /// </summary>
        public static void RemoveLocalizer(LocalizerBase localizer) => s_Handler?.RemoveLocalizer(localizer);

        #endregion
    }
}
