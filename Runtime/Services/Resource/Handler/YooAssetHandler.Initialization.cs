using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using YooAsset;
using UObject = UnityEngine.Object;
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
using WeChatWASM;
#endif

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 初始化与包管理——播放模式换算、包初始化与清单更新、下载器与缓存清理适配。
    /// </summary>
    partial class YooAssetHandler
    {
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
                case EPlayMode.OfflinePlayMode: return EResourcePlayMode.OfflinePlay;
                case EPlayMode.HostPlayMode: return EResourcePlayMode.HostPlay;
                case EPlayMode.WebPlayMode: return EResourcePlayMode.WebGLPlay;
                default: return EResourcePlayMode.OfflinePlay;
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
                case EResourcePlayMode.OfflinePlay: return EPlayMode.OfflinePlayMode;
                case EResourcePlayMode.HostPlay: return EPlayMode.HostPlayMode;
                case EResourcePlayMode.WebGLPlay: return EPlayMode.WebPlayMode;
                default: return EPlayMode.OfflinePlayMode;
            }
        }

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

        /// <inheritdoc />
        public override async UniTask<ResourcePackageInitResult> InitPackage(string packageName, bool needInitManifest = false)
        {
            LogUtility.Warning("Resource Service Used :{0}", ResourceServiceSettings.PlayMode);

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
                    LogUtility.Error("Unsupported play mode : {0}", ResourceServiceSettings.PlayMode);
                    initSource.TrySetResult(null);
                    throw new GameException(StringUtility.Format("Unsupported play mode : {0}", ResourceServiceSettings.PlayMode));
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
            if (ResourceServiceSettings.PlayMode is EResourcePlayMode.HostPlay or EResourcePlayMode.WebGLPlay && string.IsNullOrEmpty(HostServerURL))
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
#if !UNITY_EDITOR && UNITY_WEBGL && WEIXINMINIGAME
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
    }
}
