using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表处理器抽象基类（策略模式抽象策略）。定义 <see cref="ConfigTableService"/> 外观调用的配置表后端契约。
    /// <para>游戏侧的配置表生成代码继承本类，并通过 <c>ConfigTableService.Handler = new XxxConfigTableServiceHandler()</c> 安装。</para>
    /// <para>未安装自定义处理器时使用默认实现 <see cref="DefaultConfigTableHandler"/>（记录错误并返回空结果）。</para>
    /// </summary>
    [Serializable]
    public abstract class ConfigTableServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        public abstract Dictionary<string, List<string>> GetAllLocalizedStrings();

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public abstract UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken);

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        public abstract string GetUIWindowLocation(string id);
    }
}
