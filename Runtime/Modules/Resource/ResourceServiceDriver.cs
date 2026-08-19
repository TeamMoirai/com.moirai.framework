using System;
using UnityEngine;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源组件。
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceServiceDriver : MonoBehaviour
    {
        #region 属性 [PROPERTIES]

        private const int DEFAULT_PRIORITY = 0;

        private IResourceService _resourceService;

        private bool _forceUnloadUnusedAssets = false;

        private bool _preorderUnloadUnusedAssets = false;

        private bool _performGCCollect = false;

        private AsyncOperation _asyncOperation = null;

        private float _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;

        [SerializeField] private float m_MinUnloadUnusedAssetsInterval = 60f;

        [SerializeField] private float m_MaxUnloadUnusedAssetsInterval = 300f;

        [SerializeField] private bool m_UseSystemUnloadUnusedAssets = true;

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        public string PackageVersion { set; get; }

        /// <summary>
        /// 资源包名称。
        /// </summary>
        [SerializeField] private string m_PackageName = "DefaultPackage";

        /// <summary>
        /// 资源包名称。
        /// </summary>
        public string PackageName
        {
            get => m_PackageName;
            set => m_PackageName = value;
        }

        /// <summary>
        /// 资源系统运行模式。
        /// </summary>
        [SerializeField] private EPlayMode m_PlayMode = EPlayMode.EditorSimulateMode;

#if UNITY_EDITOR
        public const string EDITOR_PLAY_MODE_KEY = "EditorPlayMode";
#endif
        /// <summary>
        /// 资源系统运行模式。
        /// <remarks>编辑器内优先使用。</remarks>
        /// </summary>
        public EPlayMode PlayMode
        {
            get
            {
#if UNITY_EDITOR
                // 编辑器模式使用。
                return (EPlayMode)UnityEditor.EditorPrefs.GetInt(EDITOR_PLAY_MODE_KEY);
#else
                if (m_PlayMode == EPlayMode.EditorSimulateMode)
                {
                    m_PlayMode = EPlayMode.OfflinePlayMode;
                }
                // 运行时使用。
                return m_PlayMode;
#endif
            }
            set
            {
                m_PlayMode = value;
            }
        }
        
        [SerializeField] private EncryptionType m_EncryptionType = EncryptionType.None;
        
        /// <summary>
        /// 资源服务的加密类型。
        /// </summary>
        public EncryptionType EncryptionType => m_EncryptionType;

        /// <summary>
        /// 是否支持边玩边下载。
        /// </summary>
        [SerializeField] internal bool m_UpdatableWhilePlaying = false;

        /// <summary>
        /// 是否支持边玩边下载。
        /// </summary>
        public bool UpdatableWhilePlaying => m_UpdatableWhilePlaying;
        
        [SerializeField] internal long m_Milliseconds = 30;
        /// <summary>
        /// 设置异步系统参数，每帧执行消耗的最大时间切片（单位：毫秒）
        /// </summary>
        public long Milliseconds
        {
            get => m_Milliseconds;
            set => m_Milliseconds = value;
        }

        /// <summary>
        /// 自动释放资源引用计数为0的资源包
        /// </summary>
        [SerializeField] internal bool m_AutoUnloadBundleWhenUnused = false;

        [SerializeField] private int m_DownloadingMaxNum = 10;

        /// <summary>
        /// 获取或设置同时最大下载数目。
        /// </summary>
        public int DownloadingMaxNum
        {
            get => m_DownloadingMaxNum;
            set => m_DownloadingMaxNum = value;
        }

        [SerializeField] private int m_FailedTryAgain = 3;
        /// <summary>
        /// 获取或设置下载失败重试次数。
        /// </summary>
        public int FailedTryAgain
        {
            get => m_FailedTryAgain;
            set => m_FailedTryAgain = value;
        }

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion => _resourceService.ApplicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public int InternalResourceVersion => _resourceService.InternalResourceVersion;

        /// <summary>
        /// 获取或设置无用资源释放的最小间隔时间，以秒为单位。
        /// </summary>
        public float MinUnloadUnusedAssetsInterval
        {
            get => m_MinUnloadUnusedAssetsInterval;
            set => m_MinUnloadUnusedAssetsInterval = value;
        }

        /// <summary>
        /// 获取或设置无用资源释放的最大间隔时间，以秒为单位。
        /// </summary>
        public float MaxUnloadUnusedAssetsInterval
        {
            get => m_MaxUnloadUnusedAssetsInterval;
            set => m_MaxUnloadUnusedAssetsInterval = value;
        }

        /// <summary>
        /// 使用系统释放无用资源策略。
        /// </summary>
        public bool UseSystemUnloadUnusedAssets
        {
            get => m_UseSystemUnloadUnusedAssets;
            set => m_UseSystemUnloadUnusedAssets = value;
        }

        /// <summary>
        /// 获取无用资源释放的等待时长，以秒为单位。
        /// </summary>
        public float LastUnloadUnusedAssetsOperationElapseSeconds => _lastUnloadUnusedAssetsOperationElapseSeconds;

        [SerializeField] private float m_AssetAutoReleaseInterval = 60f;

        [SerializeField] private int m_AssetCapacity = 64;

        [SerializeField] private float m_AssetExpireTime = 60f;

        [SerializeField] private int m_AssetPriority = 0;

        /// <summary>
        /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        public float AssetAutoReleaseInterval
        {
            get => _resourceService.AssetAutoReleaseInterval;
            set => _resourceService.AssetAutoReleaseInterval = m_AssetAutoReleaseInterval = value;
        }

        /// <summary>
        /// 获取或设置资源对象池的容量。
        /// </summary>
        public int AssetCapacity
        {
            get => _resourceService.AssetCapacity;
            set => _resourceService.AssetCapacity = m_AssetCapacity = value;
        }

        /// <summary>
        /// 获取或设置资源对象池对象过期秒数。
        /// </summary>
        public float AssetExpireTime
        {
            get => _resourceService.AssetExpireTime;
            set => _resourceService.AssetExpireTime = m_AssetExpireTime = value;
        }

        /// <summary>
        /// 获取或设置资源对象池的优先级。
        /// </summary>
        public int AssetPriority
        {
            get => _resourceService.AssetPriority;
            set => _resourceService.AssetPriority = m_AssetPriority = value;
        }

        #endregion

        private void Start()
        {
            _resourceService = GameServices.GetService<IResourceService>();
            if (_resourceService == null)
            {
                LogUtility.Fatal("Resource service is invalid.");
                return;
            }

            if (PlayMode == EPlayMode.EditorSimulateMode)
            {
                LogUtility.Info("During this run, ResourceService will use editor resource files, which you should validate first.");
#if !UNITY_EDITOR
                PlayMode = EPlayMode.OfflinePlayMode;
#endif
            }

            _resourceService.DefaultPackageName = PackageName;
            _resourceService.PlayMode = PlayMode;
            _resourceService.EncryptionType = m_EncryptionType;
            _resourceService.Milliseconds = m_Milliseconds;
            _resourceService.AutoUnloadBundleWhenUnused = m_AutoUnloadBundleWhenUnused;
            _resourceService.HostServerURL = UpdateSettings.GetResDownLoadPath();
            _resourceService.FallbackHostServerURL = UpdateSettings.GetFallbackResDownLoadPath();
            _resourceService.LoadResWayWebGL = UpdateSettings.LoadResWayWebGL;
            _resourceService.DownloadingMaxNum = DownloadingMaxNum;
            _resourceService.FailedTryAgain = FailedTryAgain;
            _resourceService.UpdatableWhilePlaying = UpdatableWhilePlaying;
            _resourceService.Initialize();
            _resourceService.AssetAutoReleaseInterval = m_AssetAutoReleaseInterval;
            _resourceService.AssetCapacity = m_AssetCapacity;
            _resourceService.AssetExpireTime = m_AssetExpireTime;
            _resourceService.AssetPriority = m_AssetPriority;
            _resourceService.SetForceUnloadUnusedAssetsAction(ForceUnloadUnusedAssets);
            LogUtility.Info($"ResourceService Run Mode：{PlayMode}");
        }

        #region 释放资源 [RELEASE RESOURCES]

        /// <summary>
        /// 强制执行释放未被使用的资源。
        /// </summary>
        /// <param name="performGCCollect">是否使用垃圾回收。</param>
        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssets = true;
            if (performGCCollect)
            {
                _performGCCollect = true;
            }
        }


        private void Update()
        {
            _lastUnloadUnusedAssetsOperationElapseSeconds += UnityEngine.Time.unscaledDeltaTime;
            if (_asyncOperation == null && (_forceUnloadUnusedAssets || _lastUnloadUnusedAssetsOperationElapseSeconds >= m_MaxUnloadUnusedAssetsInterval ||
                                            _preorderUnloadUnusedAssets && _lastUnloadUnusedAssetsOperationElapseSeconds >= m_MinUnloadUnusedAssetsInterval))
            {
                LogUtility.Info("Unload unused assets...");
                _forceUnloadUnusedAssets = false;
                _preorderUnloadUnusedAssets = false;
                _lastUnloadUnusedAssetsOperationElapseSeconds = 0f;
                _asyncOperation = Resources.UnloadUnusedAssets();
                if (m_UseSystemUnloadUnusedAssets)
                {
                    _resourceService.UnloadUnusedAssets();
                }
            }

            if (_asyncOperation is { isDone: true })
            {
                _asyncOperation = null;
                if (_performGCCollect)
                {
                    LogUtility.Info("GC.Collect...");
                    _performGCCollect = false;
                    GC.Collect();
                }
            }
        }

        #endregion
    }
}