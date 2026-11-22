using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    [FrameworkSetting("[服务]对象池设置", "GameObject 的实例化/回收/按策略销毁。", -400)]
    public sealed class ObjectPoolServiceSettings : FrameworkSettings<ObjectPoolServiceSettings>
    {
        [InfoBox("默认使用内置游戏对象池实现（分页槽位 + 代系句柄 + 最小堆维护调度）。可替换为自定义对象池后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private IObjectPoolService m_ObjectPoolServiceHandler = DefaultHandler;

        /// <summary>获取配置的游戏对象池处理器（字段为 null 时懒填充并缓存——不重复实例化）。</summary>
        public static IObjectPoolService ObjectPoolServiceHandler =>
            Instance.m_ObjectPoolServiceHandler ??= DefaultHandler;

        /// <summary>获取默认对象池实现。</summary>
        private static IObjectPoolService DefaultHandler => new DefaultObjectPoolHandler();
    }
}