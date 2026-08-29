using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    [FrameworkSetting("[服务]GameObject 对象池设置", "GameObject 的实例化/回收/按策略销毁。", -390)]
    public sealed class GameObjectPoolServiceSettings : FrameworkSettings<GameObjectPoolServiceSettings>
    {
        [InfoBox("默认使用内置游戏对象池实现（分页槽位 + 代系句柄 + 最小堆维护调度）。可替换为自定义对象池后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private GameObjectPoolServiceHandler m_GameObjectPoolServiceHandler = new DefaultGameObjectPoolHandler();

        /// <summary>
        /// 获取配置的游戏对象池处理器。
        /// </summary>
        public static GameObjectPoolServiceHandler GameObjectPoolServiceHandler => Instance.m_GameObjectPoolServiceHandler;
    }
}