using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表服务外观（Facade）。
    /// <para>统一的静态配置表访问入口，通过替换 <see cref="Handler"/> 即可在不同配置表后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="ConfigTableServiceSettings"/> 解析；settings 未配置则回退 <see cref="CreateDefaultHandler"/>。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
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
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——懒加载主路径（settings 已配置时 <see cref="CreateDefaultHandler"/> 被短路）首次访问即完成世界注册。</para>
        /// </summary>
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
        /// <para>调试面板显式豁免：本服务无运行时轮询状态可供观察，不注册 Profiler 窗口。</para>
        /// <para>依赖 <see cref="ResourceService"/>：配置表后端（如游戏侧 Luban 处理器）的表数据经资源系统装载，
        /// 首次读取表内容需要资源服务已就绪——该依赖必须显式声明，否则初始化序会退化为注册序（历史故障：
        /// 本地化服务先于资源服务初始化时触发首次读表，装载失败并使处理器停留在半初始化状态）。</para>
        /// <para>被 <see cref="Localization.LocalizationService"/> 反向依赖（本地化默认数据源即配置表）。</para>
        /// </summary>
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
        /// 编辑器预览取数用的处理器：运行期已注册那份优先，未注册时直读 <see cref="ConfigTableServiceSettings"/>
        /// 里配置的实例——不装进 <c>s_Handler</c>、不调 <c>Internal_Init</c>，预览因此不需要服务世界。
        /// <para>运行期一族静态查询读的是 <c>s_Handler</c>，非播放态恒为空：预览走它们等于永远拿不到表，
        /// 而"表未生成"这种露法会把一个已生成的工程说成没生成。</para>
        /// </summary>
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
