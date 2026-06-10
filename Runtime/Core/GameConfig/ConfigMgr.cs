using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    public class ConfigMgr : Singleton<ConfigMgr>
    {
        public IConfigHelper ConfigHelper { get; set; }

        /// <summary>
        /// 从配置表获取所有多语言文本。
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, List<string>> GetAllLocalizedStrings()
        {
            if (ConfigHelper != null) return ConfigHelper.GetAllLocalizedStrings();
            
            Log.Error("Generate Config first!");
            return null;
        }

        /// <summary>
        /// 根据 ID 从配置表加载图标。
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async UniTask<Sprite> LoadSpriteByID(string id, CancellationToken cancellationToken = default) => await ConfigHelper.LoadSpriteByID(id, cancellationToken);

        /// <summary>
        /// 根据 ID 从配置表获取弹窗资产的位置。
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public string GetUIWindowLocation(string id) => ConfigHelper.GetUIWindowLocation(id);
    }
}
  