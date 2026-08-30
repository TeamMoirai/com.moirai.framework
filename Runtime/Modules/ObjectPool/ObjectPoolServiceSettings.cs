using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    [FrameworkSetting("[服务]对象池设置", "注册、Spawn、Despawn、释放继承自的 ObjectBase 的对象。", -400)]
    public sealed class ObjectPoolServiceSettings : FrameworkSettings<ObjectPoolServiceSettings>
    {
        [InfoBox("默认使用内置通用对象池实现（分页槽位 + 按名链 + 最小堆维护调度）。可替换为自定义池后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ObjectPoolServiceHandlerConfig m_HandlerConfig = new DefaultObjectPoolHandlerConfig();

        /// <summary>
        /// 获取配置的通用对象池后端配置（纯数据，经 <see cref="ObjectPoolServiceHandlerConfig.CreateHandler"/> 创建处理器实例）。
        /// </summary>
        public static ObjectPoolServiceHandlerConfig ObjectPoolServiceHandlerConfig => Instance.m_HandlerConfig;
    }
}
