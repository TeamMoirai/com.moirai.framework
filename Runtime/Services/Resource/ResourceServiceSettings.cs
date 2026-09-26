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
#if !UNITY_EDITOR
        // getter 不回写资产值，所以"EditorSimulate 要归一"这个判定每次读取都会走到；
        // 本标志只保证那条 Error 打一次。进程内单次判定即可，无需跨域重载复位。
        private static bool s_OfflineFallbackReported;
#endif
        /// <summary>
        /// 资源运行模式。玩家构建里 <see cref="EResourcePlayMode.EditorSimulate"/> 只在本属性的
        /// **读取结果**上归一为 <see cref="EResourcePlayMode.OfflinePlay"/>，资产里配置的原值保持不变
        /// （要看配置原值，读检视面板或另存一份）；归一会在首次读取时打一次 Error。
        /// </summary>
        public static EResourcePlayMode PlayMode
        {
            get
            {
#if !UNITY_EDITOR
                if (Instance.m_PlayMode == EResourcePlayMode.EditorSimulate)
                {
                    // EditorSimulate 只在编辑器里有意义，玩家构建里它只能整体退回离线。
                    // 但"退回离线"等于关掉全部远程热更，而此前它一行日志都不打：
                    // 该远程拉包的包就这么整包离线发行，而 OnInit 那行 Run Mode 读到的
                    // 恰恰是被改写之后的值，运维看不出资产里原本写了什么。
                    if (!s_OfflineFallbackReported)
                    {
                        s_OfflineFallbackReported = true;
                        LogUtility.Error("ResourceServiceSettings.m_PlayMode is EditorSimulate, which only exists in the " +
                            "editor; falling back to OfflinePlay for this player build, so NO remote resource will be " +
                            "fetched. Set it to HostPlay/WebPlay in " +
                            "Assets/Settings/Framework/Resources/ResourceServiceSettings.asset to enable hot update.");
                    }

                    // 只在读取处归一，不回写 Instance.m_PlayMode：那等于让一次 getter
                    // 悄悄改掉一份共享的 ScriptableObject 实例，把"运维本该发现的配置错误"
                    // 洗成一份看起来本来就对的资产。
                    return EResourcePlayMode.OfflinePlay;
                }
#endif
                return Instance.m_PlayMode;
            }
            set => Instance.m_PlayMode = value;
        }

        [BoxGroup(BASE_GROUP)]
        [InfoBox("默认使用内置资源后端。可替换为自定义资源后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ResourceServiceHandler m_ResourceServiceHandler = ResourceService.CreateDefaultHandler();
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

        [LabelText("销毁态轮转每帧查验数量")]
        [Tooltip("兜底回收每帧各查验多少个所有者槽位与绑定槽位。场景卸载与退出播放时 OnDestroy 的销毁派发会被截断，" +
                 "这些槽位连同其租约只能靠本配额轮转回收；调小会让回收延迟到 ceil(槽位数/配额) 帧，期间资源无法卸载。")]
        [SerializeField] private int m_DestroySweepBudget = 64;
        /// <summary>销毁态兜底回收每帧查验的槽位数量（所有者与绑定各一份）。</summary>
        public static int DestroySweepBudget => Instance.m_DestroySweepBudget;

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

        [LabelText("无引用资源句柄空闲过期时间(秒)")]
        [SerializeField] private float m_IdleAssetExpireTime = 60f;
        /// <summary>无引用资源句柄空闲过期秒数。</summary>
        public static float IdleAssetExpireTime => Instance.m_IdleAssetExpireTime;

        [Tooltip("空闲资源记录的容量上限，超出即淘汰最长空闲的那条；0 表示不留空闲记录")]
        [LabelText("空闲资源记录容量上限")]
        [MinValue(0)]
        [SerializeField] private int m_IdleAssetCapacity = 256;
        /// <summary>空闲资源记录容量上限。</summary>
        public static int IdleAssetCapacity => Instance.m_IdleAssetCapacity;

        #endregion
    }
}
