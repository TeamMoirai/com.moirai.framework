using System;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表服务配置抽象基类（纯数据，无行为无生命周期）。
    /// <para>以 <see cref="UnityEngine.SerializeReference"/> 存于 <see cref="ConfigTableServiceSettings"/> 资产；
    /// 经 <see cref="CreateHandler"/> 工厂创建绑定的后端处理器实例，处理器不再被序列化。</para>
    /// </summary>
    [Serializable]
    public abstract class ConfigTableServiceHandlerConfig
    {
        /// <summary>
        /// 创建配置绑定的配置表后端处理器实例。
        /// </summary>
        /// <returns>新的配置表处理器实例。</returns>
        public abstract ConfigTableServiceHandler CreateHandler();
    }

    /// <summary>
    /// 配置表处理器抽象基类（策略模式抽象策略）。定义 <see cref="ConfigTableService"/> 外观调用的配置表后端契约。
    /// <para>游戏侧的配置表生成代码继承本类，并通过 <c>ConfigTableService.Handler = new XxxConfigTableServiceHandler()</c> 安装。</para>
    /// <para>未安装自定义处理器时使用默认实现 <see cref="DefaultConfigTableHandler"/>（记录错误并返回空结果）。</para>
    /// <para>配置数据由 <see cref="ConfigTableServiceHandlerConfig"/> 系列纯数据类承载——处理器实例本身不再被序列化，由 <see cref="ConfigTableServiceHandlerConfig.CreateHandler"/> 工厂在运行期创建。</para>
    /// </summary>
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
