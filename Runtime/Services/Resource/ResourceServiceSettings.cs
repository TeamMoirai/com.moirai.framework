using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    [FrameworkSetting("[服务]资源设置", "资源加载、缓存与绑定后端配置", -490)]
    public sealed partial class ResourceServiceSettings : FrameworkSettings<ResourceServiceSettings>
    {
        internal const string BASE_GROUP = "基础设置";
        
        [BoxGroup(BASE_GROUP)]
        [LabelText("资源运行模式")]
        [SerializeField] private EResourcePlayMode m_PlayMode = EResourcePlayMode.EditorSimulate;
        /// <summary>
        /// 资源运行模式（非编辑器下 EditorSimulate 自动回退为 OfflinePlay）。
        /// </summary>
        public static EResourcePlayMode PlayMode
        {
            get
            {
#if !UNITY_EDITOR
                if (Instance.m_PlayMode == EResourcePlayMode.EditorSimulate)
                {
                    Instance.m_PlayMode = EResourcePlayMode.OfflinePlay;
                }
#endif
                return Instance.m_PlayMode;
            }
            set => Instance.m_PlayMode = value;
        }

        [BoxGroup(BASE_GROUP)]
        [InfoBox("默认使用内置资源后端。可替换为自定义资源后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ResourceServiceHandler m_ResourceServiceHandler = new YooAssetHandler();
        /// <summary>资源处理器（后端）。</summary>
        public static ResourceServiceHandler ResourceServiceHandler => Instance.m_ResourceServiceHandler;
        
        #region GC 与卸载 [GC AND UNLOAD]

        [Title("资源回收设置")]
        [InfoBox("合理配置资源池参数可以有效管理内存使用")]

        [Tooltip("启用自动资源回收")]
        [LabelText("使用资源模块卸载回收资源")]
        [SerializeField] private bool m_UseSystemUnloadUnusedAssets = true;
        /// <summary>使用系统释放无用资源策略。</summary>
        public static bool UseSystemUnloadUnusedAssets => Instance.m_UseSystemUnloadUnusedAssets;
        
        [Tooltip("资源回收的最小时间间隔")]
        [LabelText("最小回收间隔(秒)")]
        [ShowIf(nameof(m_UseSystemUnloadUnusedAssets))]
        [Range(0f, 3600f)]
        [SerializeField] private float m_MinUnloadUnusedAssetsInterval = 60f;
        /// <summary>无用资源释放的最小间隔时间（秒）。</summary>
        public static float MinUnloadUnusedAssetsInterval => Instance.m_MinUnloadUnusedAssetsInterval;

        [Tooltip("资源回收的最大时间间隔")]
        [LabelText("最大回收间隔(秒)")]
        [ShowIf(nameof(m_UseSystemUnloadUnusedAssets))]        
        [Range(0f, 3600f)]
        [SerializeField] private float m_MaxUnloadUnusedAssetsInterval = 300f;
        /// <summary>无用资源释放的最大间隔时间（秒）。</summary>
        public static float MaxUnloadUnusedAssetsInterval => Instance.m_MaxUnloadUnusedAssetsInterval;
        
        [Title("GC 与卸载")]
        
        [LabelText("GC 回收的最小间隔")]
        [SerializeField] private float m_MinGCCollectInterval = 30f;
        /// <summary>GC 回收的最小间隔时间（秒）。</summary>
        public static float MinGCCollectInterval => Instance.m_MinGCCollectInterval;

        [LabelText("每帧过期处理数量")]
        [SerializeField] private int m_ExpireProcessCountPerFrame = 16;
        /// <summary>每帧过期处理数量。</summary>
        public static int ExpireProcessCountPerFrame => Instance.m_ExpireProcessCountPerFrame;

        [LabelText("卸载时过期处理数量")]
        [SerializeField] private int m_ExpireProcessCountWhenUnloading = 256;
        /// <summary>卸载时过期处理数量。</summary>
        public static int ExpireProcessCountWhenUnloading => Instance.m_ExpireProcessCountWhenUnloading;

        #endregion

        #region 记录与租约 [RECORDS AND LEASE]

        [Title("记录与租约")]
        
        [LabelText("资源记录预热容量")]
        [SerializeField] private int m_AssetRecordCapacity = 64;
        /// <summary>资源记录预热容量。</summary>
        public static int AssetRecordCapacity => Instance.m_AssetRecordCapacity;

        [LabelText("资源租约预热容量")]
        [SerializeField] private int m_AssetLeaseCapacity = 128;
        /// <summary>资源租约预热容量。</summary>
        public static int AssetLeaseCapacity => Instance.m_AssetLeaseCapacity;

        [LabelText("绑定所有者预热容量")]
        [SerializeField] private int m_BindingOwnerCapacity = 64;
        /// <summary>绑定所有者预热容量。</summary>
        public static int BindingOwnerCapacity => Instance.m_BindingOwnerCapacity;

        [LabelText("绑定槽位预热容量")]
        [SerializeField] private int m_BindingSlotCapacity = 128;
        /// <summary>绑定槽位预热容量。</summary>
        public static int BindingSlotCapacity => Instance.m_BindingSlotCapacity;

        [LabelText("已注册目标预热容量")]
        [SerializeField] private int m_RegisteredTargetCapacity = 128;
        /// <summary>已注册目标预热容量。</summary>
        public static int RegisteredTargetCapacity => Instance.m_RegisteredTargetCapacity;

        [LabelText("无引用资源句柄空闲过期时间(秒)")]
        [SerializeField] private float m_IdleAssetExpireTime = 60f;
        /// <summary>无引用资源句柄空闲过期秒数。</summary>
        public static float IdleAssetExpireTime => Instance.m_IdleAssetExpireTime;

        #endregion
    }
}
