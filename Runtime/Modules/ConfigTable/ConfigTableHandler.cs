using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 配置表处理器基类。
    /// <para>游戏侧的配置表生成代码继承本类，并通过 <c>ConfigMgr.Handler = new XxxConfigTableHandler()</c> 安装。</para>
    /// <para>未安装自定义处理器时使用默认实现（记录错误并返回空结果）。</para>
    /// </summary>
    [Serializable]
    public class ConfigTableHandler : FrameworkHandler
    {
        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        public virtual Dictionary<string, List<string>> GetAllLocalizedStrings()
        {
            LogUtility.Error("Generate Config first!");
            return null;
        }

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public virtual UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken = default)
        {
            LogUtility.Error("Generate Config first!");
            return UniTask.FromResult<Sprite>(null);
        }

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        public virtual string GetUIWindowLocation(string id)
        {
            LogUtility.Error("Generate Config first!");
            return string.Empty;
        }
    }
}
