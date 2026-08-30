using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
using WeChatWASM;
#endif

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源管理器处理器——承载资源加载、缓存、租约与绑定等全部实现逻辑。
    /// <para>由 <see cref="ResourceServiceSettings"/> 序列化配置，<see cref="ResourceService"/> 外观转发调用。</para>
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    [Serializable]
    public sealed partial class YooAssetHandler : ResourceServiceHandler
    {
        #region 基础属性 [BASE PROPERTIES]

        #region YooAsset 专有配置 [YOOASSET CONFIG]

        [SerializeField] private string m_PackageName = "DefaultPackage";

        /// <inheritdoc />
        public override string DefaultPackageName
        {
            get => m_PackageName;
            set => m_PackageName = value;
        }

        [SerializeField] private EPlayMode m_PlayMode = EPlayMode.EditorSimulateMode;

#if UNITY_EDITOR
        /// <summary>编辑器运行模式的 EditorPrefs 键。</summary>
        public const string EDITOR_PLAY_MODE_KEY = "EditorPlayMode";
#endif

        /// <summary>
        /// YooAsset 运行模式（非编辑器下 EditorSimulateMode 自动回退为 OfflinePlayMode）。
        /// </summary>
        public EPlayMode YooPlayMode
        {
            get
            {
#if UNITY_EDITOR
                return (EPlayMode)UnityEditor.EditorPrefs.GetInt(EDITOR_PLAY_MODE_KEY);
#else
                if (m_PlayMode == EPlayMode.EditorSimulateMode)
                {
                    m_PlayMode = EPlayMode.OfflinePlayMode;
                }
                return m_PlayMode;
#endif
            }
            set => m_PlayMode = value;
        }

        /// <inheritdoc />
        public override EResourcePlayMode PlayMode
        {
            get => ToFrameworkPlayMode(YooPlayMode);
            set => m_PlayMode = ToYooAssetPlayMode(value);
        }

        [ProviderDropdown]
        [SerializeReference] private YooAssetEncryptorHandler m_EncryptorHandler;

        /// <summary>
        /// 资源加解密处理器（YooAsset 专有）。
        /// </summary>
        public YooAssetEncryptorHandler EncryptorHandler => m_EncryptorHandler;

        [SerializeField] private int m_DownloadingMaxNum = 10;

        /// <inheritdoc />
        public override int DownloadingMaxNum
        {
            get => m_DownloadingMaxNum;
            set => m_DownloadingMaxNum = value;
        }

        [SerializeField] private int m_FailedTryAgain = 3;

        /// <inheritdoc />
        public override int FailedTryAgain
        {
            get => m_FailedTryAgain;
            set => m_FailedTryAgain = value;
        }

        [SerializeField] private bool m_UpdatableWhilePlaying = false;

        /// <inheritdoc />
        public override bool UpdatableWhilePlaying => m_UpdatableWhilePlaying;

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

        [SerializeField] private bool m_AutoUnloadBundleWhenUnused = false;

        /// <inheritdoc />
        public override bool AutoUnloadBundleWhenUnused
        {
            get => m_AutoUnloadBundleWhenUnused;
            set => m_AutoUnloadBundleWhenUnused = value;
        }

        #endregion

        /// <summary>
        /// 绑定服务。
        /// </summary>
        public override IResourceBindingService BindingService => _bindingService;

        private ResourceBindingService _bindingService;

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

        #region 框架抽象适配 [FRAMEWORK ADAPTERS]

        /// <summary>
        /// 构建框架初始化结果。
        /// </summary>
        private static ResourcePackageInitResult BuildInitResult(string packageName, InitializePackageOperation operation)
        {
            return new ResourcePackageInitResult
            {
                PackageName = packageName,
                Operation = operation != null ? new YooAssetOperationAdapter(operation) : null,
            };
        }

        /// <summary>
        /// YooAsset 包版本请求结果：版本号实时透读底层操作。
        /// <para>YooAsset 的 <c>RequestPackageVersionOperation.PackageVersion</c> 仅在操作完成后有值——
        /// 若在创建期同步快照，调用方完成后读到的仍是过期空值（编辑器模拟模式实际版本为 "Simulate"）。</para>
        /// </summary>
        private sealed class YooAssetPackageVersionResult : ResourcePackageVersionResult
        {
            private readonly RequestPackageVersionOperation _operation;

            public YooAssetPackageVersionResult(RequestPackageVersionOperation operation)
            {
                _operation = operation;
            }

            /// <inheritdoc />
            public override string PackageVersion => _operation.PackageVersion;
        }

        /// <summary>
        /// YooAsset 异步操作适配器。
        /// </summary>
        private sealed class YooAssetOperationAdapter : IResourceOperation
        {
            private readonly AsyncOperationBase _operation;

            public YooAssetOperationAdapter(AsyncOperationBase operation)
            {
                _operation = operation;
            }

            public bool IsDone => _operation.Status != EOperationStatus.None && _operation.Status != EOperationStatus.Processing;
            public float Progress => _operation.Progress;
            public bool Succeed => _operation.Status == EOperationStatus.Succeeded;
            public string Error => _operation.Status == EOperationStatus.Failed ? _operation.Error : null;
        }

        /// <summary>
        /// YooAsset 下载器适配器。
        /// </summary>
        private sealed class YooAssetDownloaderAdapter : IResourceDownloader
        {
            private readonly ResourceDownloaderOperation _downloader;

            public YooAssetDownloaderAdapter(ResourceDownloaderOperation downloader)
            {
                _downloader = downloader;
            }

            public bool IsDone => _downloader.Status != EOperationStatus.Processing;
            public bool Succeed => _downloader.Status == EOperationStatus.Succeeded;
            public string Error => _downloader.Status == EOperationStatus.Failed ? _downloader.Error : null;
            public int TotalDownloadCount => _downloader.TotalDownloadCount;
            public string[] FailedFiles => Array.Empty<string>();
            public long TotalDownloadBytes => _downloader.TotalDownloadBytes;
            public long CurrentDownloadBytes => _downloader.CurrentDownloadBytes;
            public float Progress => _downloader.Progress;
            public int DownloadingMaxNumber { set { } }
            public int FailedTryAgain { set { } }
            public void BeginDownload() => _downloader.StartDownload();
            public void PauseDownload() => _downloader.PauseDownload();
            public void CancelDownload() => _downloader.CancelDownload();
        }

        /// <summary>
        /// YooAsset 运行模式 → 框架运行模式。
        /// </summary>
        private static EResourcePlayMode ToFrameworkPlayMode(EPlayMode playMode)
        {
            switch (playMode)
            {
                case EPlayMode.EditorSimulateMode: return EResourcePlayMode.EditorSimulate;
                case EPlayMode.OfflinePlayMode: return EResourcePlayMode.Offline;
                case EPlayMode.HostPlayMode: return EResourcePlayMode.HostPlay;
                case EPlayMode.WebPlayMode: return EResourcePlayMode.WebPlay;
                default: return EResourcePlayMode.Offline;
            }
        }

        /// <summary>
        /// 框架运行模式 → YooAsset 运行模式。
        /// </summary>
        private static EPlayMode ToYooAssetPlayMode(EResourcePlayMode playMode)
        {
            switch (playMode)
            {
                case EResourcePlayMode.EditorSimulate: return EPlayMode.EditorSimulateMode;
                case EResourcePlayMode.Offline: return EPlayMode.OfflinePlayMode;
                case EResourcePlayMode.HostPlay: return EPlayMode.HostPlayMode;
                case EResourcePlayMode.WebPlay: return EPlayMode.WebPlayMode;
                default: return EPlayMode.OfflinePlayMode;
            }
        }

        #endregion

        #region 内部字段 [INTERNAL FIELDS]

        /// <summary>
        /// 默认资源包。
        /// </summary>
        public ResourcePackage DefaultPackage { get; private set; }

        /// <summary>
        /// 资源包列表。
        /// </summary>
        private Dictionary<string, ResourcePackage> PackageMap { get; } = new Dictionary<string, ResourcePackage>();

        /// <summary>
        /// 资源信息列表。
        /// </summary>
        private readonly Dictionary<string, AssetInfo> _assetInfoMap = new Dictionary<string, AssetInfo>();

        /// <summary>
        /// 在途的包初始化任务（按包名去重，并发调用复用同一结果）。
        /// </summary>
        private readonly Dictionary<string, TaskCompletionSource<InitializePackageOperation>> _packageInitTasks =
            new Dictionary<string, TaskCompletionSource<InitializePackageOperation>>(StringComparer.Ordinal);

        /// <summary>
        /// 已成功初始化的包操作句柄缓存（幂等重入时返回同一句柄，避免调用方收到 null）。
        /// </summary>
        private readonly Dictionary<string, InitializePackageOperation> _packageInitOperations =
            new Dictionary<string, InitializePackageOperation>(StringComparer.Ordinal);

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override void Initialize()
        {
            // 恢复 Shutdown→Initialize 循环复用契约：处理器实例来自资产 [SerializeReference]，
            // 容器重启后"重新创建"拿到的仍是同一实例，必须复位关闭标志。
            _isDestroying = false;

            // 初始化资源系统
            YooAssets.Initialize(new YooAssetLogger());
            YooAssets.SetAsyncOperationMaxTimeSlice(Milliseconds);

            // 创建默认的资源包
            string packageName = DefaultPackageName;
            if (!YooAssets.TryGetPackage(packageName, out var defaultPackage))
            {
                defaultPackage = YooAssets.CreatePackage(packageName);
            }

            DefaultPackage = defaultPackage;

            _bindingService = new ResourceBindingService(this);
        }

        /// <summary>
        /// 关闭处理器——释放所有资源记录与在途加载操作。
        /// <para>由 <see cref="ResourceService.OnShutdown"/> 在容器关闭期调用。</para>
        /// </summary>
        protected override void OnShutdown()
        {
            _isDestroying = true;
            _assetUnloadGeneration++;
            _bindingService?.Shutdown();
            ShutdownLoadingOperations();
            ForceReleaseAllAssetRecords();
            _packageInitTasks.Clear();
            _packageInitOperations.Clear();
        }

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <inheritdoc />
        public override async UniTask<ResourcePackageInitResult> InitPackage(string packageName, bool needInitManifest = false)
        {
            LogUtility.Warning("Resource Service Used :{0}", PlayMode);

            // 并发去重：同一包名的初始化在途时，后续调用等待同一结果。
            if (_packageInitTasks.TryGetValue(packageName, out TaskCompletionSource<InitializePackageOperation> runningTask))
            {
                await runningTask.Task.AsUniTask();
                return BuildInitResult(packageName, _packageInitOperations.TryGetValue(packageName, out var cachedOp) ? cachedOp : null);
            }

            if (PackageMap.TryGetValue(packageName, out var resourcePackage))
            {
                if (resourcePackage.InitializeStatus == EOperationStatus.Succeeded)
                {
                    // 幂等重入：已初始化成功的包直接返回已完成的操作句柄，避免调用方收到 null。
                    if (_packageInitOperations.TryGetValue(packageName, out InitializePackageOperation completedOperation))
                    {
                        return BuildInitResult(packageName, completedOperation);
                    }

                    LogUtility.Warning("ResourceService has already init package : {0}", packageName);
                    return null;
                }

                // Failed 状态：YooAsset 在下次 InitializePackageAsync 内部自动复位，移除失效记录后重新初始化。
                PackageMap.Remove(packageName);
            }

            // 创建默认的资源包
            if (!YooAssets.TryGetPackage(packageName, out var package))
            {
                package = YooAssets.CreatePackage(packageName);
            }

            PackageMap[packageName] = package;

            TaskCompletionSource<InitializePackageOperation> initSource = new TaskCompletionSource<InitializePackageOperation>();
            _packageInitTasks[packageName] = initSource;
            try
            {
                InitializePackageOperation initOperation = CreateInitializationOperation(package, YooPlayMode);
                if (initOperation == null)
                {
                    // 未知运行模式：回滚本地记录并快速失败。
                    PackageMap.Remove(packageName);
                    LogUtility.Error("Unsupported play mode : {0}", PlayMode);
                    initSource.TrySetResult(null);
                    throw new GameException(StringUtility.Format("Unsupported play mode : {0}", PlayMode));
                }

                _packageInitOperations[packageName] = initOperation;

                await initOperation.ToUniTask();
                LogUtility.Info("Init resource package version : {0}", initOperation.Status);

                if (initOperation.Status != EOperationStatus.Succeeded)
                {
                    // 失败传播：移除本地记录（YooAsset 会在下次初始化时自动复位），调用方通过操作状态感知失败。
                    PackageMap.Remove(packageName);
                    _packageInitOperations.Remove(packageName);
                    LogUtility.Error("Init package failed : {0}, error : {1}", packageName, initOperation.Error);
                    initSource.TrySetResult(initOperation);
                    return BuildInitResult(packageName, initOperation);
                }

                if (needInitManifest)
                {
                    await InitPackageManifestInternal(package);
                }

                initSource.TrySetResult(initOperation);
                return BuildInitResult(packageName, initOperation);
            }
            catch (Exception)
            {
                // 异常传播：确保并发等待者不悬挂。
                PackageMap.Remove(packageName);
                _packageInitOperations.Remove(packageName);
                initSource.TrySetResult(null);
                throw;
            }
            finally
            {
                _packageInitTasks.Remove(packageName);
            }
        }

        /// <inheritdoc />
        public override async UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                packageName = DefaultPackageName;
            }

            if (!string.IsNullOrEmpty(hostServerURL))
            {
                HostServerURL = hostServerURL;
            }

            if (!string.IsNullOrEmpty(fallbackHostServerURL))
            {
                FallbackHostServerURL = fallbackHostServerURL;
            }

            // HostPlay/WebPlay 必须已有资源服务器地址：参数未传时回退到预先配置的 HostServerURL（Moirai 的配置源是属性）。
            if (PlayMode is EResourcePlayMode.HostPlay or EResourcePlayMode.WebPlay && string.IsNullOrEmpty(HostServerURL))
            {
                throw new GameException("Host server URL is invalid. Specify hostServerURL or set ResourceService.HostServerURL before initializing in HostPlay/WebPlay mode.");
            }

            ResourcePackageInitResult result = await InitPackage(packageName);

            // result == null：包已初始化成功但操作句柄缓存缺失（Shutdown→Initialize 循环复用场景），语义为已就绪。
            return result == null || result.Succeed;
        }

        /// <summary>
        /// 按运行模式创建初始化操作。
        /// </summary>
        private InitializePackageOperation CreateInitializationOperation(ResourcePackage package, EPlayMode playMode)
        {
            switch (playMode)
            {
                // 编辑器下的模拟模式
                case EPlayMode.EditorSimulateMode:
                {
                    var buildResult = EditorSimulateBuildInvoker.Build(package.PackageName, (int)EBundleType.VirtualAssetBundle);
                    var packageRoot = buildResult.PackageRootDirectory;
                    var createParameters = new EditorSimulateModeOptions();
                    createParameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    return package.InitializePackageAsync(createParameters);
                }

                // 单机运行模式
                case EPlayMode.OfflinePlayMode:
                {
                    var createParameters = new OfflinePlayModeOptions();
                    createParameters.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                    ConfigureBundleDecryptor(createParameters.BuiltinFileSystemParameters);
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    return package.InitializePackageAsync(createParameters);
                }

                // 联机运行模式
                case EPlayMode.HostPlayMode:
                {
                    IRemoteService remoteService = new RemoteService(HostServerURL, FallbackHostServerURL);
                    var createParameters = new HostPlayModeOptions();
                    createParameters.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                    ConfigureBundleDecryptor(createParameters.BuiltinFileSystemParameters);
                    createParameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
                    ConfigureBundleDecryptor(createParameters.CacheFileSystemParameters);
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    return package.InitializePackageAsync(createParameters);
                }

                // WebGL运行模式
                case EPlayMode.WebPlayMode:
                {
                    var createParameters = new WebPlayModeOptions();
                    IRemoteService remoteService = new RemoteService(HostServerURL, FallbackHostServerURL);
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
                    // 小游戏缓存根目录
                    // 注意：此处代码根据微信插件配置来填写！
                    LogUtility.Info("=======================WEIXINMINIGAME=======================");
                    // WebGL 系文件系统仅支持内存解密（WebNetworkFileSystem 链路要求 IBundleMemoryDecryptor）。
                    IBundleDecryptor wxDecryptor = EncryptorHandler?.CreateMemoryDecryptor();
                    string packageRoot = StringUtility.Concat(WeChatWASM.WX.env.USER_DATA_PATH, "/__GAME_FILE_CACHE");
                    createParameters.WebNetworkFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteService, wxDecryptor);
#else
                    LogUtility.Info("=======================UNITY_WEBGL=======================");
                    if (LoadResWayWebGL == EResourceLoadWayWebGL.Remote)
                    {
                        createParameters.WebNetworkFileSystemParameters = FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(remoteService);
                        ConfigureWebBundleDecryptor(createParameters.WebNetworkFileSystemParameters);
                    }
                    createParameters.WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
                    ConfigureWebBundleDecryptor(createParameters.WebServerFileSystemParameters);
#endif
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    return package.InitializePackageAsync(createParameters);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// 初始化包内清单（请求版本号并更新清单），失败时抛出异常以传播错误。
        /// </summary>
        private async UniTask InitPackageManifestInternal(ResourcePackage package)
        {
            // 2. 请求资源清单的版本信息
            var requestPackageVersionOperation = package.RequestPackageVersionAsync();
            await requestPackageVersionOperation;
            if (requestPackageVersionOperation.Status != EOperationStatus.Succeeded)
            {
                string errorMessage = StringUtility.Format("Request package version failed : {0}", requestPackageVersionOperation.Error);
                LogUtility.Error(errorMessage);
                throw new GameException(errorMessage);
            }

            // 3. 传入的版本信息更新资源清单
            var options = new PrefetchManifestOptions(requestPackageVersionOperation.PackageVersion, 60);
            var updatePackageManifestAsync = package.PrefetchManifestAsync(options);
            await updatePackageManifestAsync;
            if (updatePackageManifestAsync.Status == EOperationStatus.Failed)
            {
                string errorMessage = StringUtility.Format("Update package manifest failed : {0}", updatePackageManifestAsync.Error);
                LogUtility.Error(errorMessage);
                throw new GameException(errorMessage);
            }

            // 清单更新成功后失效 AssetInfo 缓存。
            _assetInfoMap.Clear();
        }

        /// <summary>
        /// 为本地文件系统（内置/沙盒）配置解密器：主解密器 + 内存兜底解密器。
        /// </summary>
        private void ConfigureBundleDecryptor(FileSystemParameters fileSystemParameters)
        {
            IBundleDecryptor bundleDecryptor = EncryptorHandler?.CreateDecryptor();
            if (bundleDecryptor == null) return;

            fileSystemParameters.AddParameter(EFileSystemParameter.AssetBundleDecryptor, bundleDecryptor);
            IBundleMemoryDecryptor fallbackDecryptor = EncryptorHandler.CreateMemoryDecryptor();
            if (fallbackDecryptor != null)
            {
                fileSystemParameters.AddParameter(EFileSystemParameter.AssetBundleFallbackDecryptor, fallbackDecryptor);
            }
        }

        /// <summary>
        /// 为 WebGL 系文件系统配置解密器：仅支持内存解密器，且不支持 AssetBundleFallbackDecryptor 参数。
        /// </summary>
        private void ConfigureWebBundleDecryptor(FileSystemParameters fileSystemParameters)
        {
            IBundleMemoryDecryptor memoryDecryptor = EncryptorHandler?.CreateMemoryDecryptor();
            if (memoryDecryptor == null) return;

            fileSystemParameters.AddParameter(EFileSystemParameter.AssetBundleDecryptor, memoryDecryptor);
        }

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <inheritdoc />
        public override string GetPackageVersion(string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            return package.GetPackageVersion();
        }

        /// <inheritdoc />
        public override ResourcePackageVersionResult RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            var options = new RequestPackageVersionOptions(appendTimeTicks, timeout);
            var operation = package.RequestPackageVersionAsync(options);
            return new YooAssetPackageVersionResult(operation)
            {
                PackageName = string.IsNullOrEmpty(customPackageName) ? DefaultPackageName : customPackageName,
                Operation = new YooAssetOperationAdapter(operation),
            };
        }

        /// <inheritdoc />
        public override void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
        {
            HostServerURL = defaultHostServer;
            FallbackHostServerURL = fallbackHostServer;
        }

        /// <inheritdoc />
        public override IResourceOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            var options = new LoadPackageManifestOptions(packageVersion, timeout);
            var operation = package.LoadPackageManifestAsync(options);
            TrackManifestUpdateOperation(operation);
            return new YooAssetOperationAdapter(operation);
        }

        /// <inheritdoc />
        public override IResourceDownloader CreateResourceDownloader(string customPackageName = "")
        {
            ResourcePackage package = GetPackageOrThrow(customPackageName);
            var options = new ResourceDownloaderOptions(DownloadingMaxNum, FailedTryAgain);
            return new YooAssetDownloaderAdapter(package.CreateResourceDownloader(options));
        }

        /// <inheritdoc />
        public override ResourceClearCacheResult ClearCacheAsync(EResourceClearMode clearMode, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            var options = ToYooAssetClearOptions(clearMode);
            var operation = package.ClearCacheAsync(options);
            return new ResourceClearCacheResult
            {
                Operation = new YooAssetOperationAdapter(operation),
            };
        }

        /// <summary>
        /// 框架清理模式 → YooAsset 清理选项。
        /// </summary>
        private static ClearCacheOptions ToYooAssetClearOptions(EResourceClearMode clearMode)
        {
            switch (clearMode)
            {
                case EResourceClearMode.ClearAllBundleFiles: return new ClearCacheOptions(ClearCacheMethods.ClearAllBundleFiles);
                case EResourceClearMode.ClearUnusedBundleFiles: return new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles);
                case EResourceClearMode.ClearWhenBundleFilesObsolete: return new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles);
                default: return new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles);
            }
        }

        /// <inheritdoc />
        public override void ClearAllBundleFiles(string customPackageName = "")
        {
            ClearCacheAsync(EResourceClearMode.ClearAllBundleFiles, customPackageName);
        }

        private ResourcePackage GetPackageOrThrow(string packageName)
        {
            ResourcePackage package = string.IsNullOrEmpty(packageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(packageName);

            if (package == null)
            {
                throw new GameException(StringUtility.Format("The package does not exist. Package Name :{0}", string.IsNullOrEmpty(packageName) ? DefaultPackageName : packageName));
            }

            return package;
        }

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <inheritdoc />
        public override void OnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        private readonly List<UnloadUnusedAssetsOperation> _unloadUnusedAssetsOperations = new List<UnloadUnusedAssetsOperation>();
        private readonly List<UnloadAllAssetsOperation> _unloadAllAssetsOperations = new List<UnloadAllAssetsOperation>();
        private readonly List<LoadPackageManifestOperation> _manifestUpdateOperations = new List<LoadPackageManifestOperation>();

        /// <inheritdoc />
        public override void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets()
        {
            UnloadUnusedAssets(false);
        }

        /// <inheritdoc />
        public override void UnloadUnusedAssets(bool force)
        {
            if (force)
            {
                ReleaseAllUnusedAssetRecords();
            }

            RemoveCompletedUnloadUnusedOperations();
            if (_unloadUnusedAssetsOperations.Count > 0)
            {
                return;
            }

            _assetInfoMap.Clear();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded })
                {
                    _unloadUnusedAssetsOperations.Add(package.UnloadUnusedAssetsAsync());
                }
            }
        }

        /// <inheritdoc />
        public override void ForceUnloadAllAssets()
        {
#if UNITY_WEBGL
            LogUtility.Warning("WebGL not support invoke {0}", nameof(ForceUnloadAllAssets));
#else
            RemoveCompletedUnloadAllOperations();
            if (_unloadAllAssetsOperations.Count > 0)
            {
                return;
            }

            unchecked
            {
                _assetUnloadGeneration++;
            }

            ShutdownLoadingOperations();
            if (_bindingService == null)
            {
                _bindingService = new ResourceBindingService(this);
            }
            else
            {
                _bindingService.Shutdown();
            }

            ForceReleaseAllAssetRecords();
            WarmupBindingRecords();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded })
                {
                    _unloadAllAssetsOperations.Add(package.UnloadAllAssetsAsync());
                }
            }
#endif
        }

        /// <inheritdoc />
        public override void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssetsAction?.Invoke(performGCCollect);
        }

        private void RemoveCompletedUnloadUnusedOperations()
        {
            for (int i = _unloadUnusedAssetsOperations.Count - 1; i >= 0; i--)
            {
                UnloadUnusedAssetsOperation operation = _unloadUnusedAssetsOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _unloadUnusedAssetsOperations.RemoveAt(i);
                }
            }
        }

        private void RemoveCompletedUnloadAllOperations()
        {
            for (int i = _unloadAllAssetsOperations.Count - 1; i >= 0; i--)
            {
                UnloadAllAssetsOperation operation = _unloadAllAssetsOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _unloadAllAssetsOperations.RemoveAt(i);
                }
            }
        }

        private void TrackManifestUpdateOperation(LoadPackageManifestOperation operation)
        {
            if (operation == null || operation.IsDone)
            {
                return;
            }

            _manifestUpdateOperations.Add(operation);
            WatchManifestUpdateOperation(operation).Forget();
        }

        private bool IsManifestUpdateInProgress()
        {
            bool inProgress = false;
            for (int i = _manifestUpdateOperations.Count - 1; i >= 0; i--)
            {
                LoadPackageManifestOperation operation = _manifestUpdateOperations[i];
                if (operation == null || operation.IsDone)
                {
                    _manifestUpdateOperations.RemoveAt(i);
                    continue;
                }

                inProgress = true;
            }

            return inProgress;
        }

        private async UniTaskVoid WatchManifestUpdateOperation(LoadPackageManifestOperation operation)
        {
            if (operation == null)
            {
                return;
            }

            while (!_isDestroying && !operation.IsDone)
            {
                await UniTask.Yield();
            }

            _manifestUpdateOperations.Remove(operation);
            _assetInfoMap.Clear();
        }

        #endregion

        #region 获取资源信息 [GET ASSET INFOS]

        /// <inheritdoc />
        public override bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(location) > 0;
        }

        /// <inheritdoc />
        public override long GetDownloadSize(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(location);
        }

        /// <summary>
        /// 将 YooAsset AssetInfo 数组转换为框架资源信息数组。
        /// </summary>
        private static ResourceAssetInfoEntry[] ConvertAssetInfos(AssetInfo[] infos)
        {
            if (infos == null || infos.Length == 0) return Array.Empty<ResourceAssetInfoEntry>();
            var entries = new ResourceAssetInfoEntry[infos.Length];
            for (int i = 0; i < infos.Length; i++)
            {
                entries[i] = ConvertAssetInfo(infos[i]);
            }
            return entries;
        }

        /// <summary>
        /// 将 YooAsset AssetInfo 转换为框架资源信息。
        /// </summary>
        private static ResourceAssetInfoEntry ConvertAssetInfo(AssetInfo info)
        {
            return new ResourceAssetInfoEntry
            {
                // YooAsset 3.x 清单恒以 AssetPath 注册定位，可寻址地址（Address）可能未分配而为空；
                // Location 优先取地址、空时回退资产路径，两者均可被 ConvertLocationToAssetInfo 解析。
                Location = string.IsNullOrEmpty(info.Address) ? info.AssetPath : info.Address,
                TypeName = info.AssetType?.Name,
            };
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string tag, string packageName = "")
        {
            return ConvertAssetInfos(GetPackageOrThrow(packageName).GetAssetInfos(tag));
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "")
        {
            return ConvertAssetInfos(GetPackageOrThrow(packageName).GetAssetInfos(tags));
        }

        /// <inheritdoc />
        public override ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            AssetInfo yooAssetInfo;
            bool cacheEnabled = !IsManifestUpdateInProgress();
            if (string.IsNullOrEmpty(packageName))
            {
                if (cacheEnabled && _assetInfoMap.TryGetValue(location, out AssetInfo cachedAssetInfo))
                {
                    return ConvertAssetInfo(cachedAssetInfo);
                }

                yooAssetInfo = DefaultPackage.GetAssetInfo(location);
                if (cacheEnabled && CanCacheAssetInfo(yooAssetInfo))
                {
                    _assetInfoMap[location] = yooAssetInfo;
                }

                return ConvertAssetInfo(yooAssetInfo);
            }

            string key = StringUtility.Concat(packageName, "/", location);
            if (cacheEnabled && _assetInfoMap.TryGetValue(key, out AssetInfo pkgCachedAssetInfo))
            {
                return ConvertAssetInfo(pkgCachedAssetInfo);
            }

            var package = GetPackageOrThrow(packageName);
            yooAssetInfo = package.GetAssetInfo(location);
            if (cacheEnabled && CanCacheAssetInfo(yooAssetInfo))
            {
                _assetInfoMap[key] = yooAssetInfo;
            }

            return ConvertAssetInfo(yooAssetInfo);
        }

        private static bool CanCacheAssetInfo(AssetInfo assetInfo)
        {
            // 负缓存门控：无效的 AssetInfo 不缓存，避免清单更新或加载时序变化后命中过期负结果。
            return assetInfo != null && assetInfo.IsValid && string.IsNullOrEmpty(assetInfo.Error);
        }

        /// <inheritdoc />
        public override EResourceHasAssetResult HasAsset(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            var package = GetPackageOrThrow(packageName);
            AssetInfo assetInfo = package.GetAssetInfo(location);
            if (assetInfo == null || !assetInfo.IsValid || !string.IsNullOrEmpty(assetInfo.Error))
            {
                return EResourceHasAssetResult.NotExist;
            }

            if (package.GetDownloadSize(location) > 0)
            {
                return EResourceHasAssetResult.AssetOnline;
            }

            return EResourceHasAssetResult.AssetOnDisk;
        }

        /// <inheritdoc />
        public override bool IsLocationValid(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).IsLocationValid(location);
        }

        #endregion

        #region 句柄获取 [HANDLE ACCESS]

        private AssetHandle GetHandleSync<T>(string location, string packageName = "") where T : Object
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            return GetPackageOrThrow(packageName).LoadAssetSync(location, assetType);
        }

        private AssetHandle GetHandleAsync<T>(string location, string packageName = "", uint priority = 0)
            where T : Object
        {
            return GetHandleAsync(location, typeof(T), packageName, priority);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "",
            uint priority = 0)
        {
            return GetPackageOrThrow(packageName).LoadAssetAsync(location, assetType, priority);
        }

        #endregion

        #region 资源加载 [ASSET LOADING]

        /// <inheritdoc />
        public override GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            if (!IsLocationValid(location, packageName))
            {
                LogUtility.Error("Could not found location [{0}].", location);
                return null;
            }

            ResourceLeaseHandle prefabLease = AcquirePrefabSourceLease(location, packageName);
            if (!prefabLease.IsValid)
            {
                return null;
            }

            if (!TryGetLeaseAsset(prefabLease, out Object prefabObject) ||
                prefabObject is not GameObject prefab)
            {
                Release(prefabLease);
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            if (instance == null)
            {
                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                Object.Destroy(instance);
                Release(prefabLease);
                return null;
            }

            return instance;
        }

        /// <inheritdoc />
        public override async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            if (!IsLocationValid(location, packageName))
            {
                LogUtility.Error("Could not found location [{0}].", location);
                return null;
            }

            ResourceLeaseHandle prefabLease = await AcquirePrefabSourceLeaseAsync(location, packageName,
                cancellationToken);
            if (!prefabLease.IsValid)
            {
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                Release(prefabLease);
                return null;
            }

            if (!TryGetLeaseAsset(prefabLease, out Object prefabObject) ||
                prefabObject is not GameObject prefab)
            {
                Release(prefabLease);
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            if (instance == null)
            {
                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                Object.Destroy(instance);
                Release(prefabLease);
                return null;
            }

            return instance;
        }

        private static uint NormalizePriority(int priority)
        {
            return (uint)Math.Max(0, priority);
        }

        #endregion

        #region 遗留 API [LEGACY API]

        /// <inheritdoc />
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        public override T LoadAsset<T>(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            Type assetType = typeof(T);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            string normalizedPackageName = NormalizePackageName(packageName);
            if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                    EResourceHandleKind.AssetHandle, out int cachedAssetId, out Object cachedAsset))
            {
                ref AssetSlot cachedSlot = ref GetAssetSlotRef(cachedAssetId);
                TryAddLegacyDirectRef(cachedAssetId, cachedSlot.Generation);
                return cachedAsset as T;
            }

            Object asset = GetOrLoadAsset(location, assetType, assetKind, normalizedPackageName);
            if (asset == null)
            {
                return null;
            }

            ulong recordKey = GetAssetRecordKey(normalizedPackageName, location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (_assetRecordsByKey.TryGetValue(recordKey, out int assetId) && IsValidAssetId(assetId))
            {
                ref AssetSlot slot = ref GetAssetSlotRef(assetId);
                TryAddLegacyDirectRef(assetId, slot.Generation);
            }

            return asset as T;
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override async UniTask LoadAsset<T>(string location, Action<T> callback, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                LogUtility.Error("Asset name is invalid.");
                return;
            }

            Type assetType = typeof(T);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
            Object asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(packageName, location, assetType, asset);
            }

            callback?.Invoke(asset as T);
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            Type assetType = typeof(T);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
            Object asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey,
                cancellationToken: cancellationToken);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(packageName, location, assetType, asset);
            }

            return asset as T;
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override async UniTask LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameException("Load asset callbacks is invalid.");
            }

            assetType ??= typeof(Object);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
            float duration = Time.time;
            Object asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey,
                NormalizePriority(priority), default, loadAssetCallbacks.LoadAssetUpdateCallback, userData);

            if (asset == null)
            {
                string errorMessage = StringUtility.Format("Can not load asset '{0}'.", location);
                loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, ELoadResourceStatus.NotReady, errorMessage, userData);
                return;
            }

            TryAddLegacyDirectRefByKey(packageName, location, assetType, asset);
            loadAssetCallbacks.LoadAssetSuccessCallback?.Invoke(location, asset, Time.time - duration, userData);
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public override async UniTask LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            Type assetType = typeof(Object);
            await LoadAssetAsync(location, assetType, priority, loadAssetCallbacks, userData, packageName);
        }

        #endregion

    }
}