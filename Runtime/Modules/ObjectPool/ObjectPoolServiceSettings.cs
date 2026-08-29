using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    [FrameworkSetting("[服务]对象池设置", "负责注册、Spawn、Despawn、释放带目标对象的 `ObjectBase`", -400)]
    public sealed class ObjectPoolServiceSettings : FrameworkSettings<ObjectPoolServiceSettings>
    {
        [InfoBox("默认使用内置通用对象池实现（分页槽位 + 按名链 + 最小堆维护调度）。可替换为自定义池后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ObjectPoolServiceHandler m_ObjectPoolServiceHandler = new DefaultObjectPoolHandler();

        /// <summary>
        /// 获取配置的通用对象池处理器。
        /// </summary>
        public static ObjectPoolServiceHandler ObjectPoolServiceHandler => Instance.m_ObjectPoolServiceHandler;
    }
}
