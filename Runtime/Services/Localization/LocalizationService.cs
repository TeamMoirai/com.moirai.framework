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
            ReplayPendingLocalizers();
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
            s_PendingLocalizers = null;
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
        public static long ResidentChars => s_Handler?.ResidentChars ?? 0;

        /// <summary>已登记的运行时覆盖层数量。</summary>
        public static int StringOverlayLayerCount => s_Handler?.StringOverlayLayerCount ?? 0;

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

        /// <summary>
        /// 强制重载本地化词条（配置表热更、远程词库下发后调用；未就绪时为 no-op）。
        /// <para>重载失败（数据源未就绪/整批拒载）保留上一份可用快照；成功换批后自动重注入全部本地化器并广播语言变更——
        /// 语言未变也会广播，词条内容可能已更新。覆盖层按契约不被换批清空。</para>
        /// </summary>
        public static void ReloadTexts() => s_Handler?.ReloadTexts();

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
        /// 根据文本 ID 获取带一个格式化参数的本地化字符串（不装箱路径，见处理器同名重载的说明）。
        /// </summary>
        public static string GetTextFromId<T1>(string id, T1 arg1) =>
            s_Handler?.GetTextFromId(id, arg1) ?? id;

        /// <summary>根据文本 ID 获取带两个格式化参数的本地化字符串（不装箱路径，见 <see cref="GetTextFromId{T1}(string,T1)"/>）。</summary>
        public static string GetTextFromId<T1, T2>(string id, T1 arg1, T2 arg2) =>
            s_Handler?.GetTextFromId(id, arg1, arg2) ?? id;

        /// <summary>根据文本 ID 获取带三个格式化参数的本地化字符串（不装箱路径，见 <see cref="GetTextFromId{T1}(string,T1)"/>）。</summary>
        public static string GetTextFromId<T1, T2, T3>(string id, T1 arg1, T2 arg2, T3 arg3) =>
            s_Handler?.GetTextFromId(id, arg1, arg2, arg3) ?? id;

        /// <summary>根据文本 ID 获取带四个格式化参数的本地化字符串（不装箱路径，见 <see cref="GetTextFromId{T1}(string,T1)"/>）。</summary>
        public static string GetTextFromId<T1, T2, T3, T4>(string id, T1 arg1, T2 arg2, T3 arg3, T4 arg4) =>
            s_Handler?.GetTextFromId(id, arg1, arg2, arg3, arg4) ?? id;

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

        // 服务就绪前注册的本地化器挂起队列：场景物体的 Awake 可能早于世界初始化，
        // 走 s_Handler?. 静默降级会让它永久不本地化且无重试——先挂起，OnInit 回灌。
        // 懒建且回灌即弃，稳态（服务就绪后）不留存、零开销
        private static List<LocalizerBase> s_PendingLocalizers;

        /// <summary>
        /// 添加本地化器。
        /// <para>服务未就绪时入挂起队列（去重），<see cref="OnInit"/> 回灌；就绪后直发处理器。</para>
        /// </summary>
        public static void AddLocalizer(LocalizerBase localizer)
        {
            if (localizer == null) return;

            var handler = s_Handler;
            if (handler != null)
            {
                handler.AddLocalizer(localizer);
                return;
            }

            s_PendingLocalizers ??= new List<LocalizerBase>(4);
            if (!s_PendingLocalizers.Contains(localizer)) s_PendingLocalizers.Add(localizer);
        }

        /// <summary>
        /// 移除本地化器。
        /// <para>命中挂起队列即取消尚未回灌的注册；否则委派处理器摘除。</para>
        /// </summary>
        public static void RemoveLocalizer(LocalizerBase localizer)
        {
            if (localizer == null) return;
            if (s_PendingLocalizers != null && s_PendingLocalizers.Remove(localizer)) return;

            s_Handler?.RemoveLocalizer(localizer);
        }

        /// <summary>
        /// 回灌挂起队列中的本地化器（<see cref="OnInit"/> 内调用；处理器未就绪时保留队列不丢注册）。
        /// </summary>
        internal static void ReplayPendingLocalizers()
        {
            var pending = s_PendingLocalizers;
            if (pending == null || pending.Count == 0) return;

            var handler = s_Handler;
            if (handler == null) return;

            s_PendingLocalizers = null;
            for (var i = 0; i < pending.Count; i++)
            {
                if (pending[i] == null) continue;
                handler.AddLocalizer(pending[i]);
            }
        }

        #endregion

        #region 订阅与运行时覆盖 [SUBSCRIPTION & OVERLAY]

        /// <summary>
        /// 以句柄订阅语言变更（与 <see cref="OnLanguageChanged"/> 同一次派发，但 Dispose 即摘除、关服自动作废）。
        /// </summary>
        /// <returns>订阅句柄；服务未就绪时返回一个立即可 Dispose 的空句柄。</returns>
        public static IDisposable SubscribeLanguageChanged(Action<Language> callback) =>
            s_Handler?.SubscribeLanguageChanged(callback) ?? LanguageChangeSubscription.Completed;

        /// <summary>
        /// 覆盖指定语言下的一批词条（运营热改文案、QA 强改、远程补丁走同一条路）。
        /// <para>叠加语义：未覆盖的词条仍取批内译文，空/仅空白值等同于不覆盖；
        /// 覆盖层不跨服务关闭存活。</para>
        /// </summary>
        /// <param name="sourceId">来源标识（诊断用，同名即同一层）。</param>
        /// <param name="language">被覆盖的语言。</param>
        /// <param name="entries">key → 新译文。</param>
        /// <returns>该层累计覆盖条数；服务未就绪、参数不合法或语言未收录时为 -1。</returns>
        public static int SetStringOverlay(string sourceId, Language language, IEnumerable<KeyValuePair<string, string>> entries) =>
            s_Handler?.SetStringOverlay(sourceId, language, entries) ?? -1;

        /// <summary>撤掉某个来源的全部覆盖（未就绪时为 false）。</summary>
        public static bool ClearStringOverlay(string sourceId) => s_Handler?.ClearStringOverlay(sourceId) ?? false;

        /// <summary>撤掉全部覆盖层。</summary>
        public static void ClearAllStringOverlays() => s_Handler?.ClearAllStringOverlays();

        #endregion
    }
}
