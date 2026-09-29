using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表服务外观（Facade）：统一的静态配置表访问入口，替换 <see cref="Handler"/> 即可在不同后端之间零成本切换。
    /// </summary>
    /// <remarks>
    /// 未显式设置处理器时，懒加载优先从 <see cref="ConfigTableServiceSettings"/> 解析，未配置则回退 <see cref="CreateDefaultHandler"/>。
    /// <see cref="Handler"/> 由 <c>HandlerHostGenerator</c> 源生成，线程安全懒加载。
    /// </remarks>
    [AutoRegisterService]
    [ServiceDependency(typeof(ResourceService))]
    [HandlerHost(typeof(ConfigTableServiceHandler))]
    public partial class ConfigTableService : ServiceBase
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认配置表处理器（settings 未配置时的代码兜底）。
        /// </summary>
        /// <returns>默认配置表处理器实例。</returns>
        internal static ConfigTableServiceHandler CreateDefaultHandler() => new DefaultConfigTableHandler();

        /// <summary>
        /// 从 <see cref="ConfigTableServiceSettings"/> 解析配置表处理器。
        /// </summary>
        /// <remarks>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等），首次访问即完成世界注册。</remarks>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static ConfigTableServiceHandler GetHandlerFromSettings()
        {
            GameServices.EnsureRegistered<ConfigTableService>();
            return ConfigTableServiceSettings.ConfigTableServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化配置表服务。由容器在构建期调用。
        /// </summary>
        /// <remarks>
        /// 依赖 <see cref="ResourceService"/> 已就绪：表数据经资源系统装载，初始化序必须排在资源服务之后。 <br />
        /// 被 <see cref="Localization.LocalizationService"/> 反向依赖（本地化默认数据源即配置表）。 <br />
        /// 本服务无运行时轮询状态，不注册 Profiler 窗口。
        /// </remarks>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭配置表服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        #endregion

        #region 配置表查询 [CONFIG QUERIES]

        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        /// <returns>多语言文本字典。</returns>
        public static Dictionary<string, List<string>> GetAllLocalizedStrings() =>
            s_Handler?.GetAllLocalizedStrings();

        /// <summary>
        /// 配置表自报的语言（Name 或 Code），顺序与 <see cref="GetAllLocalizedStrings"/> 的列顺序一致。
        /// </summary>
        /// <returns>未自报或未就绪时为空列表。</returns>
        public static IReadOnlyList<string> GetLocalizationLanguageCodes() =>
            s_Handler?.GetLocalizationLanguageCodes() ?? Array.Empty<string>();

        /// <summary>
        /// 当前配置表处理器是否支持按语言单独取列。未注册处理器时为 <c>false</c>（走整批加载）。
        /// </summary>
        public static bool SupportsPerLanguageLocalizationLoad =>
            s_Handler != null && s_Handler.SupportsPerLanguageLocalizationLoad;

        /// <summary>
        /// 按语言取一列词条（key → 译文）。仅在 <see cref="SupportsPerLanguageLocalizationLoad"/> 为真时有意义。
        /// </summary>
        /// <param name="languageCode"><see cref="GetLocalizationLanguageCodes"/> 自报的语言码。</param>
        /// <returns>未注册处理器或该语言取不到时为 <c>null</c>。</returns>
        public static Dictionary<string, string> GetLocalizedStringsByLanguage(string languageCode) =>
            s_Handler?.GetLocalizedStringsByLanguage(languageCode);

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken = default) =>
            s_Handler?.LoadSpriteByID(id, cancellationToken) ?? UniTask.FromResult<Sprite>(null);

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        public static string GetUIWindowLocation(string id) =>
            s_Handler?.GetUIWindowLocation(id);

        #endregion

#if UNITY_EDITOR
        #region 编辑器预览 [EDITOR PREVIEW]

        /// <summary>
        /// 编辑器预览取数用的处理器：运行期已注册那份优先，未注册时直读 <see cref="ConfigTableServiceSettings"/> 里配置的实例。
        /// </summary>
        /// <remarks>
        /// 不装进 <c>s_Handler</c>、不调 <c>Internal_Init</c>，预览因此不需要服务世界。 <br />
        /// 运行期一族静态查询读 <c>s_Handler</c>、非播放态恒为空，预览不可走它们（会把已生成的工程报成"表未生成"）。
        /// </remarks>
        private static ConfigTableServiceHandler PreviewHandler
            => s_Handler ?? ConfigTableServiceSettings.ConfigTableServiceHandler;

        /// <summary>编辑器预览取数：全表多语言文本，列序即 <see cref="GetLocalizationLanguageCodesForEditor"/> 的自报序。</summary>
        /// <returns>取不到时为 <c>null</c>（由调用方缓存失败并限流告警）。</returns>
        public static Dictionary<string, List<string>> GetAllLocalizedStringsForEditor() =>
            PreviewHandler?.GetAllLocalizedStrings();

        /// <summary>编辑器预览取数：与 <see cref="GetAllLocalizedStringsForEditor"/> 同源、同列序的语言自报。</summary>
        public static IReadOnlyList<string> GetLocalizationLanguageCodesForEditor() =>
            PreviewHandler?.GetLocalizationLanguageCodes() ?? Array.Empty<string>();

        #endregion
#endif
    }
}
