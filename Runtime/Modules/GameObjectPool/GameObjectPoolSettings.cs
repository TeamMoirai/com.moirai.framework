using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.GameObjectPool
{
    [FrameworkSetting("对象池设置", "游戏对象池后端配置", -454)]
    public sealed class GameObjectPoolSettings : FrameworkSettings<GameObjectPoolSettings>
    {
        [InfoBox("默认使用内置对象池实现（最小堆调度维护）。可替换为自定义对象池后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private GameObjectPoolHandler m_GameObjectPoolHandler = new GameObjectPoolHandler();

        public static GameObjectPoolHandler GameObjectPoolHandler => Instance.m_GameObjectPoolHandler;
    }
}
