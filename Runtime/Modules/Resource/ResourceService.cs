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
        public EncryptionType EncryptionType { get; set; } = EncryptionType.None;

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
        internal ResourcePackage DefaultPackage { private set; get; }

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
            YooAssets.Initialize(new ResourceLogger());
            YooAssets.SetOperationSystemMaxTimeSlice(Milliseconds);

            string packageName = DefaultPackageName;
            var defaultPackage = YooAssets.TryGetPackage(packageName);
            if (defaultPackage == null)
            {
                defaultPackage = YooAssets.CreatePackage(packageName);
                YooAssets.SetDefaultPackage(defaultPackage);
            }

            DefaultPackage = defaultPackage;

            _bindingService = new ResourceBindingService(this);
        }

        /// <inheritdoc />
        public async UniTask<InitializationOperation> InitPackage(string packageName, bool needInitMainFest = false)
        {
#if UNITY_EDITOR
            EPlayMode playMode = (EPlayMode)UnityEditor.EditorPrefs.GetInt(ResourceServiceDriver.EDITOR_PLAY_MODE_KEY);
            LogUtility.Warning("Editor Service Used :{0}", playMode);
#else
            EPlayMode playMode = (EPlayMode)PlayMode;
#endif

            if (PackageMap.TryGetValue(packageName, out var resourcePackage))
            {
                if (resourcePackage.InitializeStatus is EOperationStatus.Processing or EOperationStatus.Succeed)
                {
                    LogUtility.Error("ResourceSystem has already init package : {0}", packageName);
                    return null;
                }

                PackageMap.Remove(packageName);
            }

            var package = YooAssets.TryGetPackage(packageName);
            if (package == null)
            {
                package = YooAssets.CreatePackage(packageName);
            }

            PackageMap[packageName] = package;

            // 编辑器下的模拟模式
            InitializationOperation initializationOperation = null;
            if (playMode == EPlayMode.EditorSimulateMode)
            {
                var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                var packageRoot = buildResult.PackageRootDirectory;
                var createParameters = new EditorSimulateModeParameters();
                createParameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializeAsync(createParameters);
            }

            IDecryptionServices decryptionServices = CreateDecryptionServices();
            
            // 单机运行模式
            if (playMode == EPlayMode.OfflinePlayMode)
            {
                var createParameters = new OfflinePlayModeParameters();
                createParameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices);
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializeAsync(createParameters);
            }

            if (playMode == EPlayMode.HostPlayMode)
            {
                string defaultHostServer = HostServerURL;
                string fallbackHostServer = FallbackHostServerURL;
                IRemoteServices remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                var createParameters = new HostPlayModeParameters();
                createParameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices);
                createParameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, decryptionServices);
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializeAsync(createParameters);
            }

            if (playMode == EPlayMode.WebPlayMode)
            {
                var createParameters = new WebPlayModeParameters();
                IWebDecryptionServices webDecryptionServices = CreateWebDecryptionServices();
                string defaultHostServer = HostServerURL;
                string fallbackHostServer = FallbackHostServerURL;
                IRemoteServices remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
                LogUtility.Info("=======================WEIXINMINIGAME=======================");
                // 注意：如果有子目录，请修改此处！
                string packageRoot = StringUtility.Concat(WeChatWASM.WX.env.USER_DATA_PATH, "/__GAME_FILE_CACHE");
                createParameters.WebServerFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteServices, webDecryptionServices);
#else
                LogUtility.Info("=======================UNITY_WEBGL=======================");
                if (LoadResWayWebGL == ELoadResWayWebGL.Remote)
                {
                    createParameters.WebRemoteFileSystemParameters = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(remoteServices, webDecryptionServices);
                }
                createParameters.WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(webDecryptionServices);
#endif
                createParameters.AutoUnloadBundleWhenUnused = AutoUnloadBundleWhenUnused;
                initializationOperation = package.InitializeAsync(createParameters);
            }

            await initializationOperation.ToUniTask();

            LogUtility.Info("Init resource package version : {0}", initializationOperation?.Status);

            if (needInitMainFest)
            {
                // 2. 请求资源清单的版本信息
                var requestPackageVersionOperation = package.RequestPackageVersionAsync();
                await requestPackageVersionOperation;
                if (requestPackageVersionOperation.Status == EOperationStatus.Succeed)
                {
                    // 3. 传入的版本信息更新资源清单
                    var updatePackageManifestAsync = package.UpdatePackageManifestAsync(requestPackageVersionOperation.PackageVersion);
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

            return initializationOperation;
        }

        /// <summary>
        /// 创建解密服务。
        /// </summary>
        private IDecryptionServices CreateDecryptionServices()
        {
            return EncryptionType switch
            {
                EncryptionType.FileOffSet => new FileOffsetDecryption(),
                EncryptionType.FileStream => new FileStreamDecryption(),
                _ => null
            };
        }

        /// <summary>
        /// 创建Web解密服务。
        /// </summary>
        private IWebDecryptionServices CreateWebDecryptionServices()
        {
            return EncryptionType switch
            {
                EncryptionType.FileOffSet => new FileOffsetWebDecryption(),
                EncryptionType.FileStream => new FileStreamWebDecryption(),
                _ => null
            };
        }

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <inheritdoc />
        public string GetPackageVersion(string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            if (package == null)
            {
                return string.Empty;
            }

            return package.GetPackageVersion();
        }

        /// <inheritdoc />
        public RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false,
            int timeout = 60, string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.RequestPackageVersionAsync(appendTimeTicks, timeout);
        }

        /// <inheritdoc />
        public void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer)
        {
            HostServerURL = defaultHostServer;
            FallbackHostServerURL = fallbackHostServer;
        }

        /// <inheritdoc />
        public UpdatePackageManifestOperation UpdatePackageManifestAsync(string packageVersion,
            int timeout = 60, string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.UpdatePackageManifestAsync(packageVersion, timeout);
        }

        /// <inheritdoc />
        public ResourceDownloaderOperation Downloader { get; set; }

        /// <inheritdoc />
        public ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "")
        {
            ResourcePackage package;
            if (string.IsNullOrEmpty(customPackageName))
            {
                package = YooAssets.GetPackage(DefaultPackageName);
            }
            else
            {
                package = YooAssets.GetPackage(customPackageName);
            }

            Downloader = package.CreateResourceDownloader(DownloadingMaxNum, FailedTryAgain);
            return Downloader;
        }

        /// <inheritdoc />
        public ClearCacheFilesOperation ClearCacheFilesAsync(
            EFileClearMode clearMode = EFileClearMode.ClearUnusedBundleFiles,
            string customPackageName = "")
        {
            var package = string.IsNullOrEmpty(customPackageName)
                ? YooAssets.GetPackage(DefaultPackageName)
                : YooAssets.GetPackage(customPackageName);
            return package.ClearCacheFilesAsync(clearMode);
        }

        /// <inheritdoc />
        public void ClearAllBundleFiles(string customPackageName = "")
            => ClearCacheFilesAsync(EFileClearMode.ClearAllBundleFiles, customPackageName);

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <inheritdoc />
        public void OnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        private Action<bool> _forceUnloadUnusedAssetsAction;

        /// <inheritdoc />
        public void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <inheritdoc />
        public void UnloadUnusedAssets()
        {
            ReleaseAllUnusedAssetRecords();
            _assetInfoMap.Clear();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeed })
                {
                    package.UnloadUnusedAssetsAsync();
                }
            }
        }

        /// <inheritdoc />
        public void UnloadUnusedAssets(bool force)
        {
            if (force)
            {
                ProcessKeepAlive(Time.unscaledTime, int.MaxValue);
                ReleaseAllUnusedAssetRecords();
            }
            else
            {
                UnloadUnusedAssets();
            }
        }

        /// <inheritdoc />
        public void ForceUnloadAllAssets()
        {
#if UNITY_WEBGL
            LogUtility.Warning("WebGL not support invoke {0}", nameof(ForceUnloadAllAssets));
#else
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeed })
                {
                    package.UnloadAllAssetsAsync();
                }
            }
#endif
        }

        /// <inheritdoc />
        public void ForceUnloadUnusedAssets(bool performGCCollect)
        {
            _forceUnloadUnusedAssetsAction?.Invoke(performGCCollect);
        }

        #endregion

        #region 获取资源信息 [GET ASSET INFOS]

        /// <inheritdoc />
        public bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(location);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.IsNeedDownloadFromRemote(location);
        }

        /// <inheritdoc />
        public bool IsNeedDownloadFromRemote(AssetInfo assetInfo, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(assetInfo);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.IsNeedDownloadFromRemote(assetInfo);
        }

        /// <inheritdoc />
        public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tag);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.GetAssetInfos(tag);
        }

        /// <inheritdoc />
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tags);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.GetAssetInfos(tags);
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

                assetInfo = YooAssets.GetAssetInfo(location);
                _assetInfoMap[location] = assetInfo;
                return assetInfo;
            }

            string key = StringUtility.Concat(packageName, "/", location);
            if (_assetInfoMap.TryGetValue(key, out AssetInfo pkgAssetInfo))
            {
                return pkgAssetInfo;
            }

            var package = YooAssets.GetPackage(packageName);
            if (package == null)
            {
                throw new GameException(StringUtility.Format("The package does not exist. Package Name :{0}", packageName));
            }

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

            if (!CheckLocationValid(location))
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
        public bool CheckLocationValid(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.CheckLocationValid(location);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.CheckLocationValid(location);
        }

        #endregion

        #region 句柄获取 [HANDLE ACCESS]

        private AssetHandle GetHandleSync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetSync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, assetType);
        }

        private AssetHandle GetHandleAsync<T>(string location, string packageName = "")
            where T : UnityEngine.Object
        {
            return GetHandleAsync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetAsync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType);
        }

        /// <inheritdoc />
        public AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetSyncHandle(location, typeof(T), packageName);
        }

        /// <inheritdoc />
        public AssetHandle LoadAssetSyncHandle(string location, System.Type type, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetSync(location, type);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, type);
        }

        /// <inheritdoc />
        public AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return LoadAssetAsyncHandle(location, typeof(T), packageName);
        }

        /// <inheritdoc />
        public AssetHandle LoadAssetAsyncHandle(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetAsync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType);
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

            if (!CheckLocationValid(location, packageName))
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

            if (!CheckLocationValid(location, packageName))
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
            _bindingService?.RegisterPrefabSource(owner, prefabLease, prefab);
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

            if (!CheckLocationValid(location, packageName))
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

            if (!CheckLocationValid(location, packageName))
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

            if (!CheckLocationValid(location, packageName))
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
            _bindingService?.RegisterPrefabSource(owner, prefabLease, prefab);
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
                if (!CheckLocationValid(location, packageName))
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

        #region 设置下载系统参数 [DOWNLOAD SYSTEM CONFIG]

        /// <summary>
        /// 设置下载系统参数，自定义下载请求。
        /// </summary>
        /// <param name="downloadSystemUnityWebRequest">自定义下载器的请求委托。</param>
        public void SetDownloadSystemUnityWebRequest(UnityWebRequestDelegate downloadSystemUnityWebRequest)
        {
            YooAssets.SetDownloadSystemUnityWebRequest(downloadSystemUnityWebRequest);
        }

        #endregion
    }
}
