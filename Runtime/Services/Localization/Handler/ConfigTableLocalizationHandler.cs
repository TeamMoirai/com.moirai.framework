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
    /// </summary>
    [Serializable]
    internal class ConfigTableLocalizationHandler : LocalizationServiceHandler
    {
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
