using System;
using System.Collections.Generic;
using System.Threading;
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
    /// 资源管理器。
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    internal sealed partial class ResourceService : ServiceBase, IResourceService
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

        /// <inheritdoc />
        public override int Priority => 4;

        /// <summary>
        /// 绑定服务。
        /// </summary>
        public IResourceBindingService BindingService => _bindingService;

        private ResourceBindingService _bindingService;

        /// <summary>
        /// 无参构造——资源服务不再依赖对象池服务。
        /// </summary>
        public ResourceService()
        {
        }

        /// <inheritdoc />
        public override void OnInit() { }

        /// <inheritdoc />
        public override void Shutdown()
        {
            _isDestroying = true;
            _assetUnloadGeneration++;
            _bindingService?.Shutdown();
            ShutdownLoadingOperations();
            ForceReleaseAllAssetRecords();
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
#if UNITY_EDITOR
            // 编辑器模式使用。
            EPlayMode playMode = (EPlayMode)UnityEditor.EditorPrefs.GetInt(ResourceServiceDriver.EDITOR_PLAY_MODE_KEY, (int)EPlayMode.EditorSimulateMode);
            LogUtility.Warning("Editor Service Used :{0}", playMode);
#else
            // 运行时使用。
            EPlayMode playMode = (EPlayMode)PlayMode;
#endif

            if (PackageMap.TryGetValue(packageName, out var resourcePackage))
            {
                if (resourcePackage.InitializeStatus is EOperationStatus.Processing or EOperationStatus.Succeeded)
                {
                    LogUtility.Error("ResourceSystem has already init package : {0}", packageName);
                    return null;
                }

                PackageMap.Remove(packageName);
            }

            // 创建默认的资源包
            if (!YooAssets.TryGetPackage(packageName, out var package))
            {
                package = YooAssets.CreatePackage(packageName);
            }

            PackageMap[packageName] = package;

            InitializePackageOperation initOperation = null;

            switch (playMode)
            {
                // 编辑器下的模拟模式
                case EPlayMode.EditorSimulateMode:
                {
                    var buildResult = EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
                    var packageRoot = buildResult.PackageRootDirectory;
                    var createParameters = new EditorSimulateModeOptions();
                    createParameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    initOperation = package.InitializePackageAsync(createParameters);
                    break;
                }

                // 单机运行模式
                case EPlayMode.OfflinePlayMode:
                {
                    IBundleDecryptor decryptor = EncryptorHandler?.CreateDecryptor();
                    var createParameters = new OfflinePlayModeOptions();
                    createParameters.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                    ConfigureBundleDecryptor(createParameters.BuiltinFileSystemParameters, decryptor);
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    initOperation = package.InitializePackageAsync(createParameters);
                    break;
                }

                // 联机运行模式
                case EPlayMode.HostPlayMode:
                {
                    IBundleDecryptor decryptor = EncryptorHandler?.CreateDecryptor();
                    IRemoteService remoteService = new RemoteService(HostServerURL, FallbackHostServerURL);
                    var createParameters = new HostPlayModeOptions();
                    createParameters.BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                    ConfigureBundleDecryptor(createParameters.BuiltinFileSystemParameters, decryptor);
                    createParameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
                    ConfigureBundleDecryptor(createParameters.CacheFileSystemParameters, decryptor);
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    initOperation = package.InitializePackageAsync(createParameters);
                    break;
                }

                // WebGL运行模式
                case EPlayMode.WebPlayMode:
                {
                    var createParameters = new WebPlayModeOptions();
                    IBundleDecryptor decryptor = EncryptorHandler?.CreateDecryptor();
                    IRemoteService remoteService = new RemoteService(HostServerURL, FallbackHostServerURL);
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
                    // 小游戏缓存根目录
                    // 注意：此处代码根据微信插件配置来填写！
                    LogUtility.Info("=======================WEIXINMINIGAME=======================");
                    string packageRoot = StringUtility.Concat(WeChatWASM.WX.env.USER_DATA_PATH, "/__GAME_FILE_CACHE");
                    createParameters.WebNetworkFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteService, decryptor);
#else
                    LogUtility.Info("=======================UNITY_WEBGL=======================");
                    if (LoadResWayWebGL == ELoadResWayWebGL.Remote)
                    {
                        createParameters.WebNetworkFileSystemParameters = FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(remoteService);
                        ConfigureBundleDecryptor(createParameters.WebNetworkFileSystemParameters, decryptor);
                    }
                    createParameters.WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
                    ConfigureBundleDecryptor(createParameters.WebServerFileSystemParameters, decryptor);
#endif
                    createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                    initOperation = package.InitializePackageAsync(createParameters);
                    break;
                }
            }

            await initOperation.ToUniTask();

            LogUtility.Info("Init resource package version : {0}", initOperation?.Status);

            if (needInitManifest)
            {
                // 2. 请求资源清单的版本信息
                var requestPackageVersionOperation = package.RequestPackageVersionAsync();
                await requestPackageVersionOperation;
                if (requestPackageVersionOperation.Status == EOperationStatus.Succeeded)
                {
                    // 3. 传入的版本信息更新资源清单
                    var options = new PrefetchManifestOptions(requestPackageVersionOperation.PackageVersion, 60);
                    var updatePackageManifestAsync = package.PrefetchManifestAsync(options);
                    await updatePackageManifestAsync;
                    if (updatePackageManifestAsync.Status == EOperationStatus.Failed)
                    {
                        LogUtility.Error("Update package manifest failed : {0}", updatePackageManifestAsync.Status);
                    }
                }
                else
                {
                    LogUtility.Error("Request package version failed : {0}", requestPackageVersionOperation.Status);
                }
            }

            return initOperation;
        }

        private static void ConfigureBundleDecryptor(FileSystemParameters fileSystemParameters, IBundleDecryptor bundleDecryptor)
        {
            if (bundleDecryptor == null) return;

            fileSystemParameters.AddParameter(EFileSystemParameter.AssetBundleDecryptor, bundleDecryptor);
            if (bundleDecryptor is IBundleMemoryDecryptor fallbackDecryptor)
            {
                fileSystemParameters.AddParameter(EFileSystemParameter.AssetBundleFallbackDecryptor, fallbackDecryptor);
            }
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
            return package.LoadPackageManifestAsync(options);
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

            if (string.IsNullOrEmpty(packageName))
            {
                if (_assetInfoMap.TryGetValue(location, out AssetInfo assetInfo))
                {
                    return assetInfo;
                }

                assetInfo = DefaultPackage.GetAssetInfo(location);
                _assetInfoMap[location] = assetInfo;
                return assetInfo;
            }

            string key = StringUtility.Concat(packageName, "/", location);
            if (_assetInfoMap.TryGetValue(key, out AssetInfo pkgAssetInfo))
            {
                return pkgAssetInfo;
            }

            var package = GetPackageOrThrow(packageName);
            pkgAssetInfo = package.GetAssetInfo(location);
            _assetInfoMap[key] = pkgAssetInfo;
            return pkgAssetInfo;
        }

        /// <inheritdoc />
        public HasAssetResult HasAsset(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new GameException("Asset name is invalid.");
            }

            AssetInfo assetInfo = GetAssetInfo(location, packageName);

            if (!IsLocationValid(location))
            {
                return HasAssetResult.Valid;
            }

            if (assetInfo == null)
            {
                return HasAssetResult.NotExist;
            }

            if (IsNeedDownloadFromRemote(assetInfo))
            {
                return HasAssetResult.AssetOnline;
            }

            return HasAssetResult.AssetOnDisk;
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

        private AssetHandle GetHandleAsync<T>(string location, string packageName = "")
            where T : UnityEngine.Object
        {
            return GetHandleAsync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "")
        {
            return GetPackageOrThrow(packageName).LoadAssetAsync(location, assetType);
        }

        /// <inheritdoc />
        public AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetSyncHandle(location, typeof(T), packageName);
        }

        /// <inheritdoc />
        public AssetHandle LoadAssetSyncHandle(string location, System.Type type, string packageName = "")
        {
            return GetPackageOrThrow(packageName).LoadAssetSync(location, type);
        }

        /// <inheritdoc />
        public AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetAsyncHandle(location, typeof(T), packageName);
        }

        /// <inheritdoc />
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
