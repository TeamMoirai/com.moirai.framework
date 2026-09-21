using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using YooAsset;
using UObject = UnityEngine.Object;
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
    internal sealed partial class YooAssetHandler : ResourceServiceHandler
    {
        #region 基础属性 [BASE PROPERTIES]

        #region YooAsset 专有配置 [YOOASSET CONFIG]

        [CollectorPackageDropdown]
        [LabelText("资源包名")]
        [SerializeField] private string m_PackageName = "DefaultPackage";

        /// <inheritdoc />
        public override string DefaultPackageName
        {
            get => m_PackageName;
            set => m_PackageName = value;
        }

        /// <summary>
        /// YooAsset 运行模式（非编辑器下 EditorSimulateMode 自动回退为 OfflinePlayMode）。
        /// </summary>
        public EPlayMode YooPlayMode
        {
            get => ToYooAssetPlayMode(ResourceServiceSettings.PlayMode);
            set => ResourceServiceSettings.PlayMode = ToFrameworkPlayMode(value);
        }

        [ProviderDropdown]
        [LabelText("资源加密模式")]
        [SerializeReference] private YooAssetEncryptorHandler m_EncryptorHandler;

        /// <summary>
        /// 资源加解密处理器（YooAsset 专有）。
        /// </summary>
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
        public override bool IsInitialized => base.IsInitialized && YooAssets.IsInitialized;

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
        #region 资源回收 [ASSET RECYCLING]

        /// <inheritdoc />
        public override void OnLowMemory()
        {
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
                if (package is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true })
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
                // 这里是"清空后继续用同一实例"，走 Reset 而非终态 Shutdown。
                _bindingService.Reset();
            }

            ForceReleaseAllAssetRecords();
            WarmupBindingRecords();
            foreach (var package in PackageMap.Values)
            {
                if (package is { InitializeStatus: EOperationStatus.Succeeded, PackageValid: true })
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

        private AssetHandle GetHandleSync<T>(string location, string packageName = "") where T : UObject
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            return GetPackageOrThrow(packageName).LoadAssetSync(location, assetType);
        }

        private AssetHandle GetHandleAsync<T>(string location, string packageName = "", uint priority = 0)
            where T : UObject
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

            if (!TryGetLeaseAsset(prefabLease, out UObject prefabObject) ||
                prefabObject is not GameObject prefab)
            {
                Release(prefabLease);
                return null;
            }

            uint unloadGeneration = _assetUnloadGeneration;
            GameObject instance = UObject.Instantiate(prefab, parent);

            // 实例化会派发 Awake，其中可以重入强制回收/关停：
            // 此时 prefab 记录可能已被释放，租约不得再挂到清空过的绑定服务上。
            if (instance == null || _isDestroying || unloadGeneration != _assetUnloadGeneration)
            {
                if (instance != null)
                {
                    UObject.Destroy(instance);
                }

                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                UObject.Destroy(instance);
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

            if (!TryGetLeaseAsset(prefabLease, out UObject prefabObject) ||
                prefabObject is not GameObject prefab)
            {
                Release(prefabLease);
                return null;
            }

            // 父节点可能在等待期间被销毁：fake null 的 Transform 直接交给 Instantiate 会抛。
            if (!ReferenceEquals(parent, null) && parent == null)
            {
                Release(prefabLease);
                return null;
            }

            uint unloadGeneration = _assetUnloadGeneration;
            GameObject instance = UObject.Instantiate(prefab, parent);

            // 实例化会派发 Awake，其中可以重入强制回收/关停：
            // 此时 prefab 记录可能已被释放，租约不得再挂到清空过的绑定服务上。
            if (instance == null || _isDestroying || unloadGeneration != _assetUnloadGeneration)
            {
                if (instance != null)
                {
                    UObject.Destroy(instance);
                }

                Release(prefabLease);
                return null;
            }

            ResourceOwner owner = EnsureResourceOwner(instance);
            EResourceBindStatus bindStatus = _bindingService.RegisterPrefabSource(owner, prefabLease, prefab);
            if (bindStatus != EResourceBindStatus.Success)
            {
                UObject.Destroy(instance);
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
                    EResourceHandleKind.AssetHandle, out int cachedAssetId, out UObject cachedAsset))
            {
                ref AssetSlot cachedSlot = ref GetAssetSlotRef(cachedAssetId);
                TryAddLegacyDirectRef(cachedAssetId, cachedSlot.Generation);
                return cachedAsset as T;
            }

            UObject asset = GetOrLoadAsset(location, assetType, assetKind, normalizedPackageName);
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
            UObject asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey);
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
            UObject asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey,
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

            assetType ??= typeof(UObject);
            EResourceAssetKind assetKind = InferAssetKind(assetType);
            ulong assetLoadingKey = GetLoadingOperationKey(location, packageName, assetType, assetKind);
            float duration = Time.time;
            UObject asset = await GetOrLoadAssetAsync(location, assetType, assetKind, packageName, assetLoadingKey,
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
            Type assetType = typeof(UObject);
            await LoadAssetAsync(location, assetType, priority, loadAssetCallbacks, userData, packageName);
        }

        #endregion
    }
}
