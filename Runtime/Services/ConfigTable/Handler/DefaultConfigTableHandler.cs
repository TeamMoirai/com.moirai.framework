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
    [ProviderDisplay(title: "默认（兜底）", description: "未生成游戏侧配置代码时的占位：查表报错并返回空结果")]
    [Serializable]
    internal sealed class DefaultConfigTableHandler :
    FrameworkHandler, IConfigTableService
    {
        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        public Dictionary<string, List<string>> GetAllLocalizedStrings()
        {
            LogUtility.Error("Generate Config first!");
            return null;
        }

        public IReadOnlyList<string> GetLocalizationLanguageCodes()
        {
            LogUtility.Error("Generate Config first!");
            return Array.Empty<string>();
        }

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken)
        {
            LogUtility.Error("Generate Config first!");
            return UniTask.FromResult<Sprite>(null);
        }

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id">配置 ID。</param>
        public string GetUIWindowLocation(string id)
        {
            LogUtility.Error("Generate Config first!");
            return string.Empty;
        }
    }
}
