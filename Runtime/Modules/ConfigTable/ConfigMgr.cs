using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表管理门面（Facade）。
    /// <para>统一的静态配置表访问入口，游戏侧生成的配置表处理器通过 <see cref="Handler"/> 安装。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(ConfigTableHandler))]
    public static partial class ConfigMgr
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 创建默认配置表处理器。游戏侧生成代码后应通过 <c>ConfigMgr.Handler = new XxxConfigTableHandler()</c> 替换。
        /// </summary>
        /// <returns>默认配置表处理器实例。</returns>
        private static ConfigTableHandler CreateDefaultHandler()
        {
            return new ConfigTableHandler();
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
