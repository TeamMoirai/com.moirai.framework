using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    /// <summary>
    /// 默认配置表处理器。未安装游戏侧生成代码时的兜底实现（记录错误并返回空结果）。
    /// </summary>
    [Serializable]
    public sealed class DefaultConfigTableHandler : ConfigTableServiceHandler
    {
        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        public override Dictionary<string, List<string>> GetAllLocalizedStrings()
        {
            LogUtility.Error("Generate Config first!");
            return null;
        }

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public override UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken)
        {
            LogUtility.Error("Generate Config first!");
            return UniTask.FromResult<Sprite>(null);
        }

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        public override string GetUIWindowLocation(string id)
        {
            LogUtility.Error("Generate Config first!");
            return string.Empty;
        }
    }
}
