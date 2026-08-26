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
    /// 加载操作状态，用于跟踪异步加载的去重和等待。
    /// </summary>
    internal sealed class LoadingOperationState : MemoryObject
    {
        private AssetHandle _assetHandle;
        private SubAssetsHandle _subAssetsHandle;
        private bool _isDone;
        private bool _succeeded;
        private int _waiterCount;
        private bool _releaseRequested;

        /// <summary>
        /// 资源句柄。
        /// </summary>
        public AssetHandle AssetHandle
        {
            get => _assetHandle;
            set => _assetHandle = value;
        }

        /// <summary>
        /// 子资源句柄。
        /// </summary>
        public SubAssetsHandle SubAssetsHandle
        {
            get => _subAssetsHandle;
            set => _subAssetsHandle = value;
        }

        /// <summary>
        /// 是否完成。
        /// </summary>
        public bool IsDone => _isDone;

        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool Succeeded => _succeeded;

        /// <summary>
        /// 等待者数量。
        /// </summary>
        public int WaiterCount => _waiterCount;

        /// <summary>
        /// 是否已请求释放。
        /// </summary>
        public bool ReleaseRequested => _releaseRequested;

        /// <summary>
        /// 添加等待者。
        /// </summary>
        public void AddWaiter()
        {
            _waiterCount++;
        }

        /// <summary>
        /// 移除等待者。
        /// </summary>
        public void RemoveWaiter()
        {
            if (_waiterCount > 0)
            {
                _waiterCount--;
            }
        }

        /// <summary>
        /// 完成加载。
        /// </summary>
        /// <param name="success">是否成功。</param>
        public void Complete(bool success)
        {
            _isDone = true;
            _succeeded = success;
        }

        /// <summary>
        /// 请求释放。
        /// </summary>
        public void RequestRelease()
        {
            _releaseRequested = true;
        }

        /// <inheritdoc />
        public override void Clear()
        {
            _assetHandle = null;
            _subAssetsHandle = null;
            _isDone = false;
            _succeeded = false;
            _waiterCount = 0;
            _releaseRequested = false;
        }
    }

    /// <summary>
    /// 资源管理器处理器——承载资源加载、缓存、租约与绑定等全部实现逻辑。
    /// <para>由 <see cref="ResourceSettings"/> 序列化配置，<see cref="ResourceService"/> 门面转发调用。</para>
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public sealed partial class ResourceHandler : FrameworkHandler
    {
        #region 基础属性 [BASE PROPERTIES]

        /// <inheritdoc />
        public string DefaultPackageName { get; set; } = "DefaultPackage";

        /// <inheritdoc />
        public EPlayMode PlayMode { get; set; } = EPlayMode.OfflinePlayMode;

        /// <inheritdoc />
        public ResourceEncryptorHandler EncryptorHandler { get; set; }

        /// <inheritdoc />
        public long Milliseconds { get; set; } = 30;

        /// <inheritdoc />
        public bool AutoUnloadBundleWhenUnused { get; set; } = false;

        /// <summary>
        /// 绑定服务。
        /// </summary>
        public IResourceBindingService BindingService => _bindingService;

        private ResourceBindingService _bindingService;

        /// <summary>
        /// 关闭处理器——释放所有资源记录与在途加载操作。
        /// <para>由 <see cref="ResourceService.Shutdown"/> 在容器关闭期调用。</para>
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
        public string HostServerURL { get; set; }

        /// <inheritdoc />
        public string FallbackHostServerURL { get; set; }

        /// <inheritdoc />
        public ELoadResWayWebGL LoadResWayWebGL { get; set; }

        private string _applicableGameVersion;

        /// <inheritdoc />
        public string ApplicableGameVersion => _applicableGameVersion;

        private int _internalResourceVersion;

        /// <inheritdoc />
        public int InternalResourceVersion => _internalResourceVersion;

        /// <inheritdoc />
        public string PackageVersion { set; get; }

        /// <inheritdoc />
        public int DownloadingMaxNum { get; set; }

        /// <inheritdoc />
        public int FailedTryAgain { get; set; }

        /// <inheritdoc />
        public bool UpdatableWhilePlaying { get; set; }

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

        #region 初始化 [INITIALIZATION]

        /// <inheritdoc />
        public void Initialize()
        {
            // 初始化资源系统
            YooAssets.Initialize(new ResourceLogger());
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

        /// <inheritdoc />
        public async UniTask<InitializePackageOperation> InitPackage(string packageName, bool needInitManifest = false)
        {
            LogUtility.Warning("Resource Service Used :{0}", PlayMode);

            // 并发去重：同一包名的初始化在途时，后续调用等待同一结果。
            if (_packageInitTasks.TryGetValue(packageName, out TaskCompletionSource<InitializePackageOperation> runningTask))
            {
                return await runningTask.Task.AsUniTask();
            }

            if (PackageMap.TryGetValue(packageName, out var resourcePackage))
            {
                if (resourcePackage.InitializeStatus == EOperationStatus.Succeeded)
                {
                    // 幂等重入：已初始化成功的包直接返回已完成的操作句柄，避免调用方收到 null。
                    if (_packageInitOperations.TryGetValue(packageName, out InitializePackageOperation completedOperation))
                    {
                        return completedOperation;
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
                InitializePackageOperation initOperation = CreateInitializationOperation(package, PlayMode);
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
                    return initOperation;
                }

                if (needInitManifest)
                {
                    await InitPackageManifestInternal(package);
                }

                initSource.TrySetResult(initOperation);
                return initOperation;
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
                    if (LoadResWayWebGL == ELoadResWayWebGL.Remote)
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
        public string GetPackageVersion(string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            return package.GetPackageVersion();
        }

        /// <inheritdoc />
        public RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            var options = new RequestPackageVersionOptions(appendTimeTicks, timeout);
            return package.RequestPackageVersionAsync(options);
        }

        /// <inheritdoc />
        public void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
        {
            HostServerURL = defaultHostServer;
            FallbackHostServerURL = fallbackHostServer;
        }

        /// <inheritdoc />
        public LoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            var options = new LoadPackageManifestOptions(packageVersion, timeout);
            var operation = package.LoadPackageManifestAsync(options);
            TrackManifestUpdateOperation(operation);
            return operation;
        }

        /// <inheritdoc />
        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")
        {
            ResourcePackage package = GetPackageOrThrow(customPackageName);
            var options = new ResourceDownloaderOptions(DownloadingMaxNum, FailedTryAgain);
            return package.CreateResourceDownloader(options);
        }

        /// <inheritdoc />
        public ClearCacheOperation ClearCacheAsync(ClearCacheOptions options, string customPackageName = "")
        {
            var package = GetPackageOrThrow(customPackageName);
            return package.ClearCacheAsync(options);
        }

        /// <inheritdoc />
        public void ClearAllBundleFiles(string customPackageName = "")
        {
            var options = new ClearCacheOptions(ClearCacheMethods.ClearAllBundleFiles);
            ClearCacheAsync(options, customPackageName);
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
        public void OnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        private readonly List<UnloadUnusedAssetsOperation> _unloadUnusedAssetsOperations = new List<UnloadUnusedAssetsOperation>();
        private readonly List<UnloadAllAssetsOperation> _unloadAllAssetsOperations = new List<UnloadAllAssetsOperation>();
        private readonly List<LoadPackageManifestOperation> _manifestUpdateOperations = new List<LoadPackageManifestOperation>();

        /// <inheritdoc />
        public void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <inheritdoc />
        public void UnloadUnusedAssets()
        {
            UnloadUnusedAssets(false);
        }

        /// <inheritdoc />
        public void UnloadUnusedAssets(bool force)
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
        public void ForceUnloadAllAssets()
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
        public void ForceUnloadUnusedAssets(bool performGCCollect)
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
        public bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(location) > 0;
        }

        /// <inheritdoc />
        public bool IsNeedDownloadFromRemote(AssetInfo assetInfo, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetDownloadSize(assetInfo) > 0;
        }

        /// <inheritdoc />
        public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetAssetInfos(tag);
        }

        /// <inheritdoc />
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            return GetPackageOrThrow(packageName).GetAssetInfos(tags);
        }

        /// <inheritdoc />
        public AssetInfo GetAssetInfo(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            bool cacheEnabled = !IsManifestUpdateInProgress();
            if (string.IsNullOrEmpty(packageName))
            {
                if (cacheEnabled && _assetInfoMap.TryGetValue(location, out AssetInfo cachedAssetInfo))
                {
                    return cachedAssetInfo;
                }

                AssetInfo assetInfo = DefaultPackage.GetAssetInfo(location);
                if (cacheEnabled && CanCacheAssetInfo(assetInfo))
                {
                    _assetInfoMap[location] = assetInfo;
                }

                return assetInfo;
            }

            string key = StringUtility.Concat(packageName, "/", location);
            if (cacheEnabled && _assetInfoMap.TryGetValue(key, out AssetInfo pkgCachedAssetInfo))
            {
                return pkgCachedAssetInfo;
            }

            var package = GetPackageOrThrow(packageName);
            AssetInfo pkgAssetInfo = package.GetAssetInfo(location);
            if (cacheEnabled && CanCacheAssetInfo(pkgAssetInfo))
            {
                _assetInfoMap[key] = pkgAssetInfo;
            }

            return pkgAssetInfo;
        }

        private static bool CanCacheAssetInfo(AssetInfo assetInfo)
        {
            // 负缓存门控：无效的 AssetInfo 不缓存，避免清单更新或加载时序变化后命中过期负结果。
            return assetInfo != null && assetInfo.IsValid && string.IsNullOrEmpty(assetInfo.Error);
        }

        /// <inheritdoc />
        public EHasAssetResult HasAsset(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            AssetInfo assetInfo = GetAssetInfo(location, packageName);
            if (assetInfo == null || !assetInfo.IsValid || !string.IsNullOrEmpty(assetInfo.Error))
            {
                return EHasAssetResult.InvalidLocation;
            }

            if (IsNeedDownloadFromRemote(assetInfo, packageName))
            {
                return EHasAssetResult.AssetOnline;
            }

            return EHasAssetResult.AssetOnDisk;
        }

        /// <inheritdoc />
        public bool IsLocationValid(string location, string packageName = "")
        {
            return GetPackageOrThrow(packageName).IsLocationValid(location);
        }

        #endregion

        #region 句柄获取 [HANDLE ACCESS]

        private AssetHandle GetHandleSync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            return GetPackageOrThrow(packageName).LoadAssetSync(location, assetType);
        }

        private AssetHandle GetHandleAsync<T>(string location, string packageName = "", uint priority = 0)
            where T : UnityEngine.Object
        {
            return GetHandleAsync(location, typeof(T), packageName, priority);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "",
            uint priority = 0)
        {
            return GetPackageOrThrow(packageName).LoadAssetAsync(location, assetType, priority);
        }

        /// <inheritdoc />
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetSyncHandle(location, typeof(T), packageName);
        }

        /// <inheritdoc />
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public AssetHandle LoadAssetSyncHandle(string location, System.Type type, string packageName = "")
        {
            return GetPackageOrThrow(packageName).LoadAssetSync(location, type);
        }

        /// <inheritdoc />
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetAsyncHandle(location, typeof(T), packageName);
        }

        /// <inheritdoc />
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public AssetHandle LoadAssetAsyncHandle(string location, Type assetType, string packageName = "")
        {
            return GetPackageOrThrow(packageName).LoadAssetAsync(location, assetType);
        }

        #endregion

        #region 遗留资源加载 [LEGACY ASSET LOADING]

        /// <inheritdoc />
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        public T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAsset(location, typeof(T), packageName) as T;
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        public UnityEngine.Object LoadAsset(string location, Type assetType, string packageName = "")
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

            string normalizedPackageName = NormalizePackageName(packageName);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            Object asset = GetOrLoadAsset(location, assetType, assetKind, normalizedPackageName);
            if (asset == null)
            {
                return null;
            }

            TryAddLegacyDirectRefByKey(normalizedPackageName, location, assetType, asset);
            return asset;
        }

        /// <inheritdoc />
        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
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

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent);
            if (instance == null)
            {
                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                UnityEngine.Object.Destroy(instance);
                Release(prefabLease);
                return null;
            }

            return instance;
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public async UniTaskVoid LoadAsset<T>(string location, Action<T> callback, string packageName = "")
            where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(location))
            {
                LogUtility.Error("Asset name is invalid.");
                return;
            }

            if (!IsLocationValid(location, packageName))
            {
                LogUtility.Error("Could not found location [{0}].", location);
                callback?.Invoke(null);
                return;
            }

            string normalizedPackageName = NormalizePackageName(packageName);
            Type assetType = typeof(T);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong loadingKey = GetLoadingOperationKey(location, normalizedPackageName, assetType, assetKind);
            Object asset = await GetOrLoadAssetAsync(location, assetType, assetKind, normalizedPackageName,
                loadingKey);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(normalizedPackageName, location, assetType, asset);
            }

            callback?.Invoke(asset as T);
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public async UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default,
            string packageName = "") where T : UnityEngine.Object
        {
            return await LoadAssetAsync(location, typeof(T), cancellationToken, packageName) as T;
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public async UniTask<UnityEngine.Object> LoadAssetAsync(string location, Type assetType,
            CancellationToken cancellationToken = default, string packageName = "")
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

            string normalizedPackageName = NormalizePackageName(packageName);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong loadingKey = GetLoadingOperationKey(location, normalizedPackageName, assetType, assetKind);
            Object asset = await GetOrLoadAssetAsync(location, assetType, assetKind, normalizedPackageName,
                loadingKey, cancellationToken: cancellationToken);
            if (asset != null)
            {
                TryAddLegacyDirectRefByKey(normalizedPackageName, location, assetType, asset);
            }

            return asset;
        }

        /// <inheritdoc />
        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null,
            CancellationToken cancellationToken = default, string packageName = "")
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

            GameObject instance = UnityEngine.Object.Instantiate(prefab, parent);
            if (instance == null)
            {
                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                UnityEngine.Object.Destroy(instance);
                Release(prefabLease);
                return null;
            }

            return instance;
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public async void LoadAssetAsync(string location, Type assetType, int priority,
            LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            if (loadAssetCallbacks == null)
            {
                throw new GameException("Load asset callbacks is invalid.");
            }

            try
            {
                if (!IsLocationValid(location, packageName))
                {
                    string errorMessage = StringUtility.Format("Could not found location [{0}].", location);
                    LogUtility.Error(errorMessage);
                    loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, ELoadResourceStatus.NotExist,
                        errorMessage, userData);
                    return;
                }

                string normalizedPackageName = NormalizePackageName(packageName);
                EResourceAssetKind assetKind = InferAssetKind(assetType);
                ulong loadingKey = GetLoadingOperationKey(location, normalizedPackageName, assetType, assetKind);
                float duration = Time.time;
                Object asset = await GetOrLoadAssetAsync(location, assetType, assetKind, normalizedPackageName,
                    loadingKey, NormalizePriority(priority), default,
                    loadAssetCallbacks.LoadAssetUpdateCallback, userData);

                if (asset == null)
                {
                    string errorMessage = StringUtility.Format("Can not load asset '{0}'.", location);
                    loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, ELoadResourceStatus.NotReady,
                        errorMessage, userData);
                    return;
                }

                TryAddLegacyDirectRefByKey(normalizedPackageName, location, assetType, asset);
                loadAssetCallbacks.LoadAssetSuccessCallback?.Invoke(location, asset, Time.time - duration,
                    userData);
            }
            catch (Exception ex)
            {
                LogUtility.Error("LoadAssetAsync failed: {0}, error: {1}", location, ex);
                loadAssetCallbacks.LoadAssetFailureCallback?.Invoke(location, ELoadResourceStatus.AssetError,
                    ex.Message, userData);
            }
        }

        /// <inheritdoc />
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public async void LoadAssetAsync(string location, int priority,
            LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
        {
            LoadAssetAsync(location, typeof(UnityEngine.Object), priority, loadAssetCallbacks, userData,
                packageName);
        }

        private static uint NormalizePriority(int priority)
        {
            return (uint)Math.Max(0, priority);
        }

        #endregion

    }
}
