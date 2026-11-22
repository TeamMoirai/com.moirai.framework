using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Asset;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表服务外观（Facade）：统一静态访问入口 + 后端实例公共缓存。
    /// </summary>
    /// <remarks>
    /// 容器是唯一事实源：settings 选出的后端 handler 作为 <see cref="IConfigTableService"/> 契约服务注册进 <see cref="GameServices"/>， <br />
    /// 本类缓存该实例（懒链注册结果回填）；服务互调经 <c>GameServices.GetRequiredService&lt;IConfigTableService&gt;()</c> 解析。 <br />
    /// 未显式设置处理器时，懒加载优先从 <see cref="ConfigTableServiceSettings"/> 解析，未配置则回退 <see cref="CreateDefaultHandler"/>。
    /// <see cref="Handler"/> 由 <c>ProviderHandlerHostGenerator</c> 源生成，懒加载 + 绑定即注册。
    /// </remarks>
    [ProviderHandlerHost(typeof(IConfigTableService), typeof(DefaultConfigTableHandler))]
    public static partial class ConfigTableService
    {
        /// <summary>
        /// 从 <see cref="ConfigTableServiceSettings"/> 解析配置表处理器（懒链 settings 来源；注册与缓存回填由生成代码完成）。
        /// </summary>
        /// <remarks>表数据经资源系统装载（依赖 <see cref="Asset.AssetService"/>）；被 <see cref="Localization.LocalizationService"/> 反向依赖（本地化默认数据源即配置表）。</remarks>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static IConfigTableService GetHandlerFromSettings()
        {
            return ConfigTableServiceSettings.ConfigTableServiceHandler;
        }

        #region 配置表查询 [CONFIG QUERIES]

        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        /// <returns>多语言文本字典。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dictionary<string, List<string>> GetAllLocalizedStrings() =>
            Internal_PeekHandler()?.GetAllLocalizedStrings();

        /// <summary>
        /// 配置表自报的语言（Name 或 Code），顺序与 <see cref="GetAllLocalizedStrings"/> 的列顺序一致。
        /// </summary>
        /// <returns>未自报或未就绪时为空列表。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IReadOnlyList<string> GetLocalizationLanguageCodes() =>
            Internal_PeekHandler()?.GetLocalizationLanguageCodes() ?? Array.Empty<string>();

        /// <summary>当前配置表处理器是否支持按语言单独取列。未注册处理器时为 <c>false</c>（走整批加载）。</summary>
        public static bool SupportsPerLanguageLocalizationLoad =>
            s_Handler != null && s_Handler.SupportsPerLanguageLocalizationLoad;

        /// <summary>
        /// 按语言取一列词条（key → 译文）。仅在 <see cref="SupportsPerLanguageLocalizationLoad"/> 为真时有意义。
        /// </summary>
        /// <param name="languageCode"><see cref="GetLocalizationLanguageCodes"/> 自报的语言码。</param>
        /// <returns>未注册处理器或该语言取不到时为 <c>null</c>。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dictionary<string, string> GetLocalizedStringsByLanguage(string languageCode) =>
            Internal_PeekHandler()?.GetLocalizedStringsByLanguage(languageCode);

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken = default) =>
            Internal_PeekHandler()?.LoadSpriteByID(id, cancellationToken) ?? UniTask.FromResult<Sprite>(null);

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetUIWindowLocation(string id) =>
            Internal_PeekHandler()?.GetUIWindowLocation(id);

        #endregion

#if UNITY_EDITOR
        #region 编辑器预览 [EDITOR PREVIEW]

        /// <summary>编辑器预览取数用的处理器：运行期已注册那份优先，未注册时直读 <see cref="ConfigTableServiceSettings"/> 里配置的实例。</summary>
        /// <remarks>
        /// 不装进 <c>s_Handler</c>、不调 <c>Internal_Init</c>，预览因此不需要服务世界。 <br />
        /// 运行期一族静态查询读 <c>s_Handler</c>、非播放态恒为空，预览不可走它们（会把已生成的工程报成"表未生成"）。
        /// </remarks>
        private static IConfigTableService PreviewHandler
            => s_Handler ?? ConfigTableServiceSettings.ConfigTableServiceHandler;

        /// <summary>
        /// 编辑器预览取数：全表多语言文本，列序即 <see cref="GetLocalizationLanguageCodesForEditor"/> 的自报序。
        /// </summary>
        /// <returns>取不到时为 <c>null</c>（由调用方缓存失败并限流告警）。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dictionary<string, List<string>> GetAllLocalizedStringsForEditor() =>
            PreviewHandler?.GetAllLocalizedStrings();

        /// <summary>
        /// 编辑器预览取数：与 <see cref="GetAllLocalizedStringsForEditor"/> 同源、同列序的语言自报。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IReadOnlyList<string> GetLocalizationLanguageCodesForEditor() =>
            PreviewHandler?.GetLocalizationLanguageCodes() ?? Array.Empty<string>();

        #endregion
#endif
    }
}
