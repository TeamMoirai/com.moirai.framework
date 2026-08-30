using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    [FrameworkSetting("[服务]资源设置", "资源加载、缓存与绑定后端配置", -490)]
    public sealed partial class ResourceServiceSettings : FrameworkSettings<ResourceServiceSettings>
    {
        [InfoBox("默认使用内置资源后端。可替换为自定义资源后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ResourceServiceHandlerConfig m_HandlerConfig = new YooAssetHandlerConfig();

        /// <summary>资源后端配置（纯数据，经 <see cref="ResourceServiceHandlerConfig.CreateHandler"/> 创建处理器实例）。</summary>
        public static ResourceServiceHandlerConfig ResourceServiceHandlerConfig => Instance.m_HandlerConfig;

        #region GC 与卸载 [GC AND UNLOAD]

        [SerializeField] private float m_MinUnloadUnusedAssetsInterval = 60f;

        /// <summary>无用资源释放的最小间隔时间（秒）。</summary>
        public static float MinUnloadUnusedAssetsInterval => Instance.m_MinUnloadUnusedAssetsInterval;

        [SerializeField] private float m_MaxUnloadUnusedAssetsInterval = 300f;

        /// <summary>无用资源释放的最大间隔时间（秒）。</summary>
        public static float MaxUnloadUnusedAssetsInterval => Instance.m_MaxUnloadUnusedAssetsInterval;

        [SerializeField] private bool m_UseSystemUnloadUnusedAssets = true;

        /// <summary>使用系统释放无用资源策略。</summary>
        public static bool UseSystemUnloadUnusedAssets => Instance.m_UseSystemUnloadUnusedAssets;

        [SerializeField] private float m_MinGCCollectInterval = 30f;

        /// <summary>GC 回收的最小间隔时间（秒）。</summary>
        public static float MinGCCollectInterval => Instance.m_MinGCCollectInterval;

        [SerializeField] private int m_ExpireProcessCountPerFrame = 16;

        /// <summary>每帧过期处理数量。</summary>
        public static int ExpireProcessCountPerFrame => Instance.m_ExpireProcessCountPerFrame;

        [SerializeField] private int m_ExpireProcessCountWhenUnloading = 256;

        /// <summary>卸载时过期处理数量。</summary>
        public static int ExpireProcessCountWhenUnloading => Instance.m_ExpireProcessCountWhenUnloading;

        #endregion

        #region 记录与租约 [RECORDS AND LEASE]

        [SerializeField] private int m_AssetRecordCapacity = 64;

        /// <summary>资源记录预热容量。</summary>
        public static int AssetRecordCapacity => Instance.m_AssetRecordCapacity;

        [SerializeField] private int m_AssetLeaseCapacity = 128;

        /// <summary>资源租约预热容量。</summary>
        public static int AssetLeaseCapacity => Instance.m_AssetLeaseCapacity;

        [SerializeField] private int m_BindingOwnerCapacity = 64;

        /// <summary>绑定所有者预热容量。</summary>
        public static int BindingOwnerCapacity => Instance.m_BindingOwnerCapacity;

        [SerializeField] private int m_BindingSlotCapacity = 128;

        /// <summary>绑定槽位预热容量。</summary>
        public static int BindingSlotCapacity => Instance.m_BindingSlotCapacity;

        [SerializeField] private int m_RegisteredTargetCapacity = 128;

        /// <summary>已注册目标预热容量。</summary>
        public static int RegisteredTargetCapacity => Instance.m_RegisteredTargetCapacity;

        [SerializeField] private float m_IdleAssetExpireTime = 60f;

        /// <summary>无引用资源句柄空闲过期秒数。</summary>
        public static float IdleAssetExpireTime => Instance.m_IdleAssetExpireTime;

        #endregion
    }
}
