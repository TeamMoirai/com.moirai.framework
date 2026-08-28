using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    [FrameworkSetting("[服务]对象池设置", "游戏对象池后端配置", -400)]
    public sealed class ObjectPoolServiceSettings : FrameworkSettings<ObjectPoolServiceSettings>
    {
        [InfoBox("默认使用内置对象池实现（最小堆调度维护）。可替换为自定义对象池后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ObjectPoolServiceHandler m_ObjectPoolServiceHandler = new DefaultObjectPoolHandler();

        public static ObjectPoolServiceHandler ObjectPoolServiceHandler => Instance.m_ObjectPoolServiceHandler;
    }
}
