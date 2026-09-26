using System.Collections.Generic;
using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.ConfigTable;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 配置表数据源本地化处理器（默认实现）。
    /// <para>从 <see cref="ConfigTableService"/> 加载的多语言配置表获取语言代码与字符串字典。</para>
    /// <para><b>语言必须随表自报</b>：<c>GetLocalizationLanguageCodes</c> 返回空时本处理器产出空批，
    /// 由基类以「数据未就绪」拒载并保持重试——拒绝回落任何全局注册表，语言列序只有一个真相源。</para>
    /// <para>配置表按语言分份存储时（处理器自报 <see cref="ConfigTableService.SupportsPerLanguageLocalizationLoad"/>）
    /// 走基类的按语言列模式：常驻与取值都只有「语言头 + 当前语言列」。</para>
    /// </summary>
    [Serializable]
    internal class ConfigTableLocalizationHandler : LocalizationServiceHandler
    {
        /// <summary>
        /// 是否走按语言列模式，完全由配置表处理器的自报决定——本类不额外设门槛，
        /// 否则「表已按语言分份、服务却整批常驻」这种半启用状态无法从任何一侧解释。
        /// </summary>
        protected override bool SupportsPerLanguageLoad => ConfigTableService.SupportsPerLanguageLocalizationLoad;

        /// <summary>
        /// 语言头即表自报的语言序列，列序与整批模式的列序同源（都出自 <see cref="ConfigTableService.GetLocalizationLanguageCodes"/>）。
        /// </summary>
        protected override IReadOnlyList<Language> LoadLanguageHeader()
            => LocalizationService.ResolveLanguages(ConfigTableService.GetLocalizationLanguageCodes());

        /// <summary>
        /// 按语言取一列。返回 <c>null</c> 时基类按「未就绪」处理并保持重试。
        /// </summary>
        protected override Dictionary<string, string> LoadLanguageColumn(Language language)
            => language == null ? null : ConfigTableService.GetLocalizedStringsByLanguage(language.Code);

        /// <summary>
        /// 从配置表加载一批词条：语言由表自报，列序即自报顺序（经外观共享的 <c>ResolveLanguages</c> 解析，
        /// 与编辑器预览同一条语言解析路径）。
        /// </summary>
        internal override LocalizationTextBatch LoadLocalizedTextBatch()
        {
            var strings = ConfigTableService.GetAllLocalizedStrings();
            var languages = LocalizationService.ResolveLanguages(ConfigTableService.GetLocalizationLanguageCodes());
            return new LocalizationTextBatch(languages, strings, "config-table");
        }

        /// <summary>
        /// 异步批加载：表字节已随资源服务预加载完毕，读表本身为纯内存展开；
        /// 让出一帧，避免整表展开压在调用方的首查询帧上。
        /// </summary>
        internal override async UniTask<LocalizationTextBatch> LoadLocalizedTextBatchAsync()
        {
            await UniTask.Yield();
            return LoadLocalizedTextBatch();
        }
    }
}
