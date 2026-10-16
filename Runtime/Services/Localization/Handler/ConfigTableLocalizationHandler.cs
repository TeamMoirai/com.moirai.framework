using System.Collections.Generic;
using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.ConfigTable;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 配置表数据源本地化处理器（默认实现）：从 <see cref="ConfigTableService"/> 的多语言配置表读取语言代码与字符串字典。
    /// </summary>
    /// <remarks>
    /// 语言必须随表自报：<c>GetLocalizationLanguageCodes</c> 返回空时产出空批，由基类以「数据未就绪」拒载并保持重试，不回落任何全局注册表。 <br />
    /// 配置表按语言分份存储时（<see cref="ConfigTableService.SupportsPerLanguageLocalizationLoad"/>）走基类按语言列模式，常驻与取值都只有「语言头 + 当前语言列」。
    /// </remarks>
    [ProviderDisplay(title: "配置表数据源", description: "默认：语言代码与文本字典读自 ConfigTableService 多语言表")]
    [Serializable]
    internal class ConfigTableLocalizationHandler : LocalizationServiceHandler
    {
        /// <summary>
        /// 自报本数据源的语言：把配置表的自报序列原样交出来，非播放态也读得到。
        /// </summary>
        /// <remarks>非播放态没有已注册的配置表后端，改读 Settings 里配置的那份实例，语言候选因此不必进 Play 就能成立。
        /// 装载路径（<see cref="LoadLanguageHeader"/> 与 <see cref="LoadLocalizedTextBatch"/>）不走这里：它们只认已注册的后端，
        /// 未注册时给出空语言头，由基类按「数据未就绪」保持重试。基类只在解析那趟问一次，不在每次候选重绘时打后端。</remarks>
        protected override IReadOnlyList<string> SelfReportedLanguageCodes()
        {
#if UNITY_EDITOR
            if (!UnityEngine.Application.isPlaying) return ConfigTableService.GetLocalizationLanguageCodesForEditor();
#endif
            return ConfigTableService.GetLocalizationLanguageCodes();
        }

        /// <summary>是否走按语言列模式，完全由配置表处理器的自报决定。</summary>
        /// <remarks>本类不额外设门槛，避免出现「表已按语言分份、服务却整批常驻」的半启用状态。</remarks>
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
        /// 从配置表加载一批词条：语言由表自报，列序即自报顺序。
        /// </summary>
        /// <remarks>语言经外观共享的 <c>ResolveLanguages</c> 解析，与编辑器预览走同一条语言解析路径。</remarks>
        internal override LocalizationTextBatch LoadLocalizedTextBatch()
        {
            var strings = ConfigTableService.GetAllLocalizedStrings();
            var languages = LocalizationService.ResolveLanguages(ConfigTableService.GetLocalizationLanguageCodes());
            return new LocalizationTextBatch(languages, strings, "config-table");
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器预览取数：与非播放态的运行期取数同源，但走配置表的编辑态直读入口，不要求服务世界。
        /// </summary>
        /// <remarks>整批形态（每键一行、列序即自报序）只留给预览——预览要同时看到所有语言列；运行期走按语言列模式。
        /// 那两个编辑态入口本身只存在于编辑器编译目标里，播放器侧不覆写，基类给的「不给预览」即正确答。</remarks>
        internal override LocalizationTextBatch LoadEditorPreviewBatch()
        {
            var strings = ConfigTableService.GetAllLocalizedStringsForEditor();
            if (strings == null || strings.Count == 0) return null;

            // 语言必须随表自报：未自报就没有可对齐的列序，不回落任何全局注册表
            var codes = ConfigTableService.GetLocalizationLanguageCodesForEditor();
            if (codes == null || codes.Count == 0) return null;

            var languages = LocalizationService.ResolveLanguages(codes);
            return languages.Count == 0 ? null : new LocalizationTextBatch(languages.ToArray(), strings, "editor-preview");
        }
#endif

        /// <summary>
        /// 异步批加载：读表并让出一帧，避免整表展开压在调用方的首查询帧上。
        /// </summary>
        /// <remarks>表字节已随资源服务预加载完毕，读表本身为纯内存展开。</remarks>
        internal override async UniTask<LocalizationTextBatch> LoadLocalizedTextBatchAsync()
        {
            await UniTask.Yield();
            return LoadLocalizedTextBatch();
        }
    }
}
