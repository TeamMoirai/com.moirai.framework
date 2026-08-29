using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表服务外观（Facade）。
    /// <para>统一的静态配置表访问入口，通过替换 <see cref="Handler"/> 即可在不同配置表后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="ConfigTableServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(ConfigTableServiceHandler))]
    public partial class ConfigTableService : ServiceBase
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="ConfigTableServiceSettings"/> 创建默认配置表处理器。
        /// </summary>
        /// <returns>默认配置表处理器实例。</returns>
        private static ConfigTableServiceHandler CreateDefaultHandler()
        {
            return ConfigTableServiceSettings.ConfigTableServiceHandler;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化配置表服务。由容器在构建期调用。
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
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        #endregion

        #region 配置表查询 [CONFIG QUERIES]

        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        /// <returns>多语言文本字典。</returns>
        public static Dictionary<string, List<string>> GetAllLocalizedStrings() =>
            Handler.GetAllLocalizedStrings();

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public static UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken = default) =>
            Handler.LoadSpriteByID(id, cancellationToken);

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        public static string GetUIWindowLocation(string id) =>
            Handler.GetUIWindowLocation(id);

        #endregion
    }
}
