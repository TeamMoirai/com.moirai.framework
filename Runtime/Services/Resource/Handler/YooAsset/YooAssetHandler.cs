using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源管理器处理器：承载资源加载、缓存、租约与绑定等全部实现逻辑。
    /// </summary>
    /// <remarks>由 <see cref="ResourceServiceSettings"/> 序列化配置，<see cref="ResourceService"/> 外观转发调用。</remarks>
    // ReSharper disable once ClassNeverInstantiated.Global
    [ProviderDisplay(title: "YooAsset", description: "默认：YooAsset 资源管线（加载/缓存/租约/绑定/远程包下载）")]
    [Serializable]
    internal sealed partial class YooAssetHandler : ResourceServiceHandler, IResourceRecordHost
    {
        #region 基础属性 [BASE PROPERTIES]

        #region YooAsset 专有配置 [YOOASSET CONFIG]

        [CollectorPackageDropdown]
        [LabelText("资源包名")]
        [SerializeField] private string m_DefaultPackageName = "Default";

        /// <inheritdoc />
        public override string DefaultPackageName
        {
            get => m_DefaultPackageName;
            set => m_DefaultPackageName = value;
        }

        /// <summary>YooAsset 运行模式（非编辑器下 EditorSimulateMode 自动回退为 OfflinePlayMode）。</summary>
        public EPlayMode YooPlayMode
        {
            get => ToYooAssetPlayMode(ResourceServiceSettings.PlayMode);
            set => ResourceServiceSettings.PlayMode = ToFrameworkPlayMode(value);
        }

        [ProviderDropdown(showNone: true)]
        [LabelText("资源加密模式")]
        [SerializeReference] private YooAssetEncryptorHandler m_EncryptorHandler;

        /// <summary>资源加解密处理器（YooAsset 专有）。</summary>
        public YooAssetEncryptorHandler EncryptorHandler => m_EncryptorHandler;

        [Title("下载设置(网络下载和重试配置)")]
        [InfoBox("下载设置影响网络资源加载的效率和稳定性")]

        [Tooltip("游戏运行时允许下载资源")]
        [LabelText("允许边玩边下")]
        [SerializeField] private bool m_UpdatableWhilePlaying = false;

        /// <inheritdoc />
        public override bool UpdatableWhilePlaying => m_UpdatableWhilePlaying;

        [Tooltip("同时进行的最大下载任务数")]
        [LabelText("最大下载数量")]
        [Range(1, 48)]
        [SerializeField] private int m_DownloadingMaxNum = 10;

        /// <inheritdoc />
        public override int DownloadingMaxNum
        {
            get => m_DownloadingMaxNum;
            set => m_DownloadingMaxNum = value;
        }

        [Tooltip("下载失败时的重试次数")]
        [LabelText("失败重试次数")]
        [Range(1, 48)]
        [SerializeField] private int m_FailedTryAgain = 3;

        /// <inheritdoc />
        public override int FailedTryAgain
        {
            get => m_FailedTryAgain;
            set => m_FailedTryAgain = value;
        }

        [Title("性能调优相关配置")]

        [Tooltip("每帧处理资源操作的最大时间")]
        [LabelText("异步处理帧时间限制(毫秒)")]
        [Range(1L, 100L)]
        [SerializeField] private long m_Milliseconds = 30;

        /// <inheritdoc />
        public override long Milliseconds
        {
            get => m_Milliseconds;
            set
            {
                if (value < 0)
                {
                    throw new GameException("Async operation max time slice cannot be negative.");
                }

                m_Milliseconds = value;
                YooAssets.SetAsyncOperationMaxTimeSlice(m_Milliseconds);
            }
        }

        [Tooltip("自动释放资源引用计数为0的资源包")]
        [LabelText("自动释放资源无用资源包")]
        [SerializeField] private bool m_AutoUnloadBundleWhenUnused = false;

        /// <inheritdoc />
        public override bool AutoUnloadBundleWhenUnused
        {
            get => m_AutoUnloadBundleWhenUnused;
            set => m_AutoUnloadBundleWhenUnused = value;
        }

        #endregion

        /// <inheritdoc />
        public override bool IsInitialized => base.IsInitialized && DefaultPackage?.InitializeStatus == EOperationStatus.Succeeded;

        private ResourceBindingService _bindingService;

        /// <inheritdoc />
        public override IResourceBindingService BindingService => _bindingService;

        /// <inheritdoc />
        public override string HostServerURL { get; set; }

        /// <inheritdoc />
        public override string FallbackHostServerURL { get; set; }

        /// <inheritdoc />
        public override EResourceLoadWayWebGL LoadResWayWebGL { get; set; }

        private string _applicableGameVersion;

        /// <inheritdoc />
        public override string ApplicableGameVersion => _applicableGameVersion;

        private int _internalResourceVersion;

        /// <inheritdoc />
        public override int InternalResourceVersion => _internalResourceVersion;

        /// <inheritdoc />
        public override string PackageVersion { set; get; }

        #endregion

        #region 内部字段 [INTERNAL FIELDS]

        /// <summary>默认资源包。</summary>
        public ResourcePackage DefaultPackage { get; private set; }

        /// <summary>资源包列表。</summary>
        private Dictionary<string, ResourcePackage> PackageMap { get; } = new Dictionary<string, ResourcePackage>();

        /// <summary>资源信息列表。</summary>
        private readonly Dictionary<string, AssetInfo> _assetInfoMap = new Dictionary<string, AssetInfo>();

        /// <summary>在途的包初始化任务（按包名去重，并发调用复用同一结果）。</summary>
        private readonly Dictionary<string, TaskCompletionSource<InitializePackageOperation>> _packageInitTasks =
            new Dictionary<string, TaskCompletionSource<InitializePackageOperation>>(StringComparer.Ordinal);

        /// <summary>已成功初始化的包操作句柄缓存（幂等重入时返回同一句柄，避免调用方收到 null）。</summary>
        private readonly Dictionary<string, InitializePackageOperation> _packageInitOperations =
            new Dictionary<string, InitializePackageOperation>(StringComparer.Ordinal);

        /// <summary>在途的清单更新操作（完成后摘除，并失效资产信息缓存）。</summary>
        private readonly List<LoadPackageManifestOperation> _manifestUpdateOperations = new List<LoadPackageManifestOperation>();

        #endregion
    }
}
