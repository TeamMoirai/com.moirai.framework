using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化服务契约（策略模式契约接口）：本地化后端的唯一消费契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 默认实现为 <see cref="ConfigTableLocalizationHandler"/>，可在 <see cref="LocalizationServiceSettings"/> 中替换。 <br />
    /// 本契约与引擎基座 <see cref="LocalizationServiceHandler"/> 分层：基座承载语言切换引擎、装载管线、缺失键统计与订阅管理（1500+ 行共享实现，豁免契约化）， <br />
    /// 后端经派生基座落地；本接口只收编门面公共 API（消费面），供服务互调与单测注入。 <br />
    /// internal 协作面（偏好解析/编辑器预览 PeekText 等）不进公共契约，框架内部按引擎基座引用访问。 <br />
    /// 实现约束：实现类须继承 <see cref="LocalizationServiceHandler"/>（家族基座，挂 <see cref="Moirai.Atropos.ServiceBase"/> 生命周期——绑定即注册进容器）。 <br />
    /// 语义文档与 <see cref="LocalizationService"/> 门面对应 API 同源。
    /// </remarks>
    public interface ILocalizationService
    {
        #region 语言状态 [LANGUAGE STATE]

        /// <summary>当前生效语言。</summary>
        Language CurrentLanguage { get; }

        /// <summary>当前生效语言的下标（<see cref="LoadedLanguages"/> 序）。</summary>
        int CurrentLanguageIndex { get; }

        /// <summary>已装载语言清单。</summary>
        IReadOnlyList<Language> LoadedLanguages { get; }

        /// <summary>当前语言是否从右到左书写。</summary>
        bool IsCurrentLanguageRightToLeft { get; }

        /// <summary>数据是否已装载完成。</summary>
        bool IsDataLoaded { get; }

        /// <summary>当生效语言变化时触发（订阅走 <see cref="SubscribeLanguageChanged"/> 的 IDisposable 通道）。</summary>
        event Action<Language> onLanguageChanged;

        /// <summary>
        /// 切换到指定语言。
        /// </summary>
        void ChangeLanguage(Language language, bool logSource = false);

        /// <summary>
        /// 按语言码或名称切换。
        /// </summary>
        void ChangeLanguage(string language);

        /// <summary>
        /// 按下标切换。
        /// </summary>
        void ChangeLanguage(int index);

        /// <summary>
        /// 切换到上一支可用语言。
        /// </summary>
        string ActivatePreviousLanguage();

        /// <summary>
        /// 切换到下一支可用语言。
        /// </summary>
        string ActivateNextLanguage();

        #endregion 语言状态 [LANGUAGE STATE]

        #region 装载 [LOADING]

        /// <summary>
        /// 异步装载本地化数据（幂等且在途去重）。
        /// </summary>
        UniTask LoadAsync();

        /// <summary>
        /// 同步重载本地化文本。
        /// </summary>
        void ReloadTexts();

        /// <summary>已装载语言数量。</summary>
        int LanguageCount { get; }

        /// <summary>词条总数。</summary>
        int EntryCount { get; }

        /// <summary>常驻字符数（内存占用量级指标）。</summary>
        long ResidentChars { get; }

        #endregion 装载 [LOADING]

        #region 文本查询 [TEXT QUERIES]

        /// <summary>
        /// 词条是否存在。
        /// </summary>
        bool Has(string id);

        /// <summary>
        /// 取文本（取不到返回 false，不触发缺译统计——低频诊断用）。
        /// </summary>
        bool TryGetTextFromId(string id, out string text);

        /// <summary>
        /// 取文本（缺译露 key）。
        /// </summary>
        string GetTextFromId(string id);

        /// <summary>
        /// 取文本并格式化（缺译露 key）。
        /// </summary>
        string GetTextFromId(string id, params object[] p);

        /// <summary>
        /// 取文本并格式化（单参强类型重载）。
        /// </summary>
        string GetTextFromId<T1>(string id, T1 arg1);

        /// <summary>
        /// 取文本并格式化（双参强类型重载）。
        /// </summary>
        string GetTextFromId<T1, T2>(string id, T1 arg1, T2 arg2);

        /// <summary>
        /// 取文本并格式化（三参强类型重载）。
        /// </summary>
        string GetTextFromId<T1, T2, T3>(string id, T1 arg1, T2 arg2, T3 arg3);

        /// <summary>
        /// 取文本并格式化（四参强类型重载）。
        /// </summary>
        string GetTextFromId<T1, T2, T3, T4>(string id, T1 arg1, T2 arg2, T3 arg3, T4 arg4);

        /// <summary>
        /// 按指定语言取文本（不切当前语言）。
        /// </summary>
        string GetTextFromIdLanguage(string id, Language language);

        /// <summary>
        /// 按指定语言取文本并格式化（不切当前语言）。
        /// </summary>
        string GetTextFromIdLanguage(string id, Language language, params object[] p);

        /// <summary>
        /// 取词条字典（整表键值）。
        /// </summary>
        Dictionary<string, string> GetDictionaryFromId(string id);

        /// <summary>
        /// 取全部词条 id。
        /// </summary>
        List<string> GetAllIds();

        /// <summary>
        /// 取复数文本。
        /// </summary>
        string GetPluralTextFromId(string id, long count, params object[] p);

        #endregion 文本查询 [TEXT QUERIES]

        #region 缺失键统计 [MISSING KEYS]

        /// <summary>缺译键数量。</summary>
        int MissingKeyCount { get; }

        /// <summary>缺译事件计数。</summary>
        int MissingKeyEventCount { get; }

        /// <summary>
        /// 取缺译键快照。
        /// </summary>
        string[] GetMissingKeys();

        /// <summary>
        /// 清空缺译键统计。
        /// </summary>
        void ClearMissingKeys();

        #endregion 缺失键统计 [MISSING KEYS]

        #region 本地化器与订阅 [LOCALIZERS & SUBSCRIPTIONS]

        /// <summary>
        /// 登记本地化器（装载完成后自动重注入）。
        /// </summary>
        void AddLocalizer(LocalizerBase localizer);

        /// <summary>
        /// 移除本地化器。
        /// </summary>
        void RemoveLocalizer(LocalizerBase localizer);

        /// <summary>
        /// 订阅语言变化，返回退订句柄。
        /// </summary>
        IDisposable SubscribeLanguageChanged(Action<Language> callback);

        #endregion 本地化器与订阅 [LOCALIZERS & SUBSCRIPTIONS]

        #region 字符串覆层 [STRING OVERLAY]

        /// <summary>当前覆层数量。</summary>
        int StringOverlayLayerCount { get; }

        /// <summary>
        /// 压入一层字符串覆层（返回层句柄）。
        /// </summary>
        int SetStringOverlay(string sourceId, Language language, IEnumerable<KeyValuePair<string, string>> entries);

        /// <summary>
        /// 摘除指定来源的覆层。
        /// </summary>
        bool ClearStringOverlay(string sourceId);

        /// <summary>
        /// 摘除全部覆层。
        /// </summary>
        void ClearAllStringOverlays();

        #endregion 字符串覆层 [STRING OVERLAY]

        #region 导出语言 [EXPORTED LANGUAGES]

        /// <summary>导出语言码清单（数据源自报）。</summary>
        IReadOnlyList<string> ExportedLanguageCodes { get; }

        /// <summary>导出语言清单（数据源自报）。</summary>
        IReadOnlyList<Language> ExportedLanguages { get; }

        #endregion 导出语言 [EXPORTED LANGUAGES]
    }
}
