using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源管理器门面（Facade），为游戏提供统一的资源加载、缓存、租约与绑定接口。
    /// <para>统一的静态资源访问入口，通过替换 <see cref="Handler"/> 即可在不同资源后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="ResourceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(ResourceHandler))]
    public partial class ResourceService : ServiceBase
    {
        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="ResourceSettings"/> 创建默认资源处理器。
        /// </summary>
        /// <returns>默认资源处理器实例。</returns>
        private static ResourceHandler CreateDefaultHandler()
        {
            return ResourceSettings.ResourceHandler;
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化资源服务。由容器在构建期调用，触发 <see cref="Handler"/> 懒加载。
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭资源服务。由容器在关闭期调用。
        /// </summary>
        public override void Shutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        /// <inheritdoc />
        public override int Priority => 4;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        /// <summary>
        /// 默认资源包名称。
        /// </summary>
        public static string DefaultPackageName
        {
            get => s_Handler?.DefaultPackageName;
            set { if (s_Handler != null) s_Handler.DefaultPackageName = value; }
        }

        /// <summary>
        /// 运行模式。
        /// </summary>
        public static EPlayMode PlayMode
        {
            get => s_Handler?.PlayMode ?? EPlayMode.OfflinePlayMode;
            set { if (s_Handler != null) s_Handler.PlayMode = value; }
        }

        /// <summary>
        /// 资源加解密处理器。
        /// </summary>
        public static ResourceEncryptorHandler EncryptorHandler
        {
            get => s_Handler?.EncryptorHandler;
            set { if (s_Handler != null) s_Handler.EncryptorHandler = value; }
        }

        /// <summary>
        /// 异步系统每帧执行消耗的最大时间切片（单位：毫秒）。
        /// </summary>
        public static long Milliseconds
        {
            get => s_Handler?.Milliseconds ?? 30;
            set { if (s_Handler != null) s_Handler.Milliseconds = value; }
        }

        /// <summary>
        /// 自动释放资源引用计数为 0 的资源包。
        /// </summary>
        public static bool AutoUnloadBundleWhenUnused
        {
            get => s_Handler?.AutoUnloadBundleWhenUnused ?? false;
            set { if (s_Handler != null) s_Handler.AutoUnloadBundleWhenUnused = value; }
        }

        /// <summary>
        /// 资源绑定服务。
        /// </summary>
        public static IResourceBindingService BindingService => s_Handler?.BindingService;

        /// <summary>
        /// 默认资源包。
        /// </summary>
        public static ResourcePackage DefaultPackage => s_Handler?.DefaultPackage;

        /// <summary>
        /// 热更 URL，资源服务器地址。
        /// </summary>
        public static string HostServerURL
        {
            get => s_Handler?.HostServerURL;
            set { if (s_Handler != null) s_Handler.HostServerURL = value; }
        }

        /// <summary>
        /// 备用热更 URL。
        /// </summary>
        public static string FallbackHostServerURL
        {
            get => s_Handler?.FallbackHostServerURL;
            set { if (s_Handler != null) s_Handler.FallbackHostServerURL = value; }
        }

        /// <summary>
        /// WebGL 平台加载本地资源/加载远程资源。
        /// </summary>
        public static ELoadResWayWebGL LoadResWayWebGL
        {
            get => s_Handler?.LoadResWayWebGL ?? default;
            set { if (s_Handler != null) s_Handler.LoadResWayWebGL = value; }
        }

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public static string ApplicableGameVersion => s_Handler?.ApplicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public static int InternalResourceVersion => s_Handler?.InternalResourceVersion ?? 0;

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        public static string PackageVersion
        {
            get => s_Handler?.PackageVersion;
            set { if (s_Handler != null) s_Handler.PackageVersion = value; }
        }

        /// <summary>
        /// 同时下载的最大数目。
        /// </summary>
        public static int DownloadingMaxNum
        {
            get => s_Handler?.DownloadingMaxNum ?? 0;
            set { if (s_Handler != null) s_Handler.DownloadingMaxNum = value; }
        }

        /// <summary>
        /// 失败重试最大数目。
        /// </summary>
        public static int FailedTryAgain
        {
            get => s_Handler?.FailedTryAgain ?? 0;
            set { if (s_Handler != null) s_Handler.FailedTryAgain = value; }
        }

        /// <summary>
        /// 是否边玩边下载。
        /// </summary>
        public static bool UpdatableWhilePlaying
        {
            get => s_Handler?.UpdatableWhilePlaying ?? false;
            set { if (s_Handler != null) s_Handler.UpdatableWhilePlaying = value; }
        }

        #endregion

        #region 容量与过期 [CAPACITY & EXPIRY]

        /// <summary>
        /// 资源记录预热容量。
        /// </summary>
        public static int AssetRecordCapacity
        {
            get => s_Handler?.AssetRecordCapacity ?? 0;
            set { if (s_Handler != null) s_Handler.AssetRecordCapacity = value; }
        }

        /// <summary>
        /// 资源租约预热容量。
        /// </summary>
        public static int AssetLeaseCapacity
        {
            get => s_Handler?.AssetLeaseCapacity ?? 0;
            set { if (s_Handler != null) s_Handler.AssetLeaseCapacity = value; }
        }

        /// <summary>
        /// 绑定所有者预热容量。
        /// </summary>
        public static int BindingOwnerCapacity
        {
            get => s_Handler?.BindingOwnerCapacity ?? 0;
            set { if (s_Handler != null) s_Handler.BindingOwnerCapacity = value; }
        }

        /// <summary>
        /// 绑定槽位预热容量。
        /// </summary>
        public static int BindingSlotCapacity
        {
            get => s_Handler?.BindingSlotCapacity ?? 0;
            set { if (s_Handler != null) s_Handler.BindingSlotCapacity = value; }
        }

        /// <summary>
        /// 已注册目标预热容量。
        /// </summary>
        public static int RegisteredTargetCapacity
        {
            get => s_Handler?.RegisteredTargetCapacity ?? 0;
            set { if (s_Handler != null) s_Handler.RegisteredTargetCapacity = value; }
        }

        /// <summary>
        /// 无引用资源句柄进入 Idle 后的过期秒数。
        /// </summary>
        public static float IdleAssetExpireTime
        {
            get => s_Handler?.IdleAssetExpireTime ?? 60f;
            set { if (s_Handler != null) s_Handler.IdleAssetExpireTime = value; }
        }

        /// <summary>
        /// 资源自动释放检查间隔（秒）。
        /// </summary>
        public static float AssetAutoReleaseInterval
        {
            get => s_Handler?.AssetAutoReleaseInterval ?? 60f;
            set { if (s_Handler != null) s_Handler.AssetAutoReleaseInterval = value; }
        }

        /// <summary>
        /// 资源容量上限。
        /// </summary>
        public static int AssetCapacity
        {
            get => s_Handler?.AssetCapacity ?? 0;
            set { if (s_Handler != null) s_Handler.AssetCapacity = value; }
        }

        /// <summary>
        /// 资源过期秒数。
        /// </summary>
        public static float AssetExpireTime
        {
            get => s_Handler?.AssetExpireTime ?? 60f;
            set { if (s_Handler != null) s_Handler.AssetExpireTime = value; }
        }

        /// <summary>
        /// 资源池优先级。
        /// </summary>
        public static int AssetPriority
        {
            get => s_Handler?.AssetPriority ?? 0;
            set { if (s_Handler != null) s_Handler.AssetPriority = value; }
        }

        /// <summary>
        /// 预热资源记录。
        /// </summary>
        public static void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity) =>
            s_Handler?.WarmupResourceRecords(assetCapacity, leaseCapacity, unityObjectIndexCapacity);

        /// <summary>
        /// 批量获取资源信息。
        /// </summary>
        public static int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount) =>
            s_Handler?.GetAssetInfos(results, startIndex, maxCount) ?? 0;

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化资源系统（YooAssets 初始化、创建默认资源包与绑定服务）。
        /// </summary>
        public static void Initialize() => s_Handler?.Initialize();

        /// <summary>
        /// 初始化资源包。
        /// </summary>
        public static UniTask<InitializePackageOperation> InitPackage(string customPackageName, bool needInitManifest = false) =>
            s_Handler?.InitPackage(customPackageName, needInitManifest) ?? UniTask.FromResult<InitializePackageOperation>(null);

        #endregion

        #region 租约 API [LEASE API]

        /// <summary>
        /// 使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        public static ResourceLeaseHandle AcquireDirect(ResourceKey key) =>
            s_Handler?.AcquireDirect(key) ?? ResourceLeaseHandle.Invalid;

        /// <summary>
        /// 异步获取一个直接资源租约。
        /// </summary>
        public static UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default) =>
            s_Handler?.AcquireDirectAsync(key, cancellationToken) ?? UniTask.FromResult(ResourceLeaseHandle.Invalid);

        /// <summary>
        /// 尝试使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        public static bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)
        {
            if (s_Handler != null)
            {
                return s_Handler.TryAcquireDirect(key, out handle);
            }

            handle = ResourceLeaseHandle.Invalid;
            return false;
        }

        /// <summary>
        /// 释放一个显式资源租约。
        /// </summary>
        public static void Release(ResourceLeaseHandle handle) => s_Handler?.Release(handle);

        /// <summary>
        /// 同步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : UnityEngine.Object =>
            s_Handler?.LoadLease<T>(key) ?? default;

        /// <summary>
        /// 同步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadLease<T>(location, packageName) ?? default;

        /// <summary>
        /// 异步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default) where T : UnityEngine.Object =>
            s_Handler?.LoadLeaseAsync<T>(key, cancellationToken) ?? UniTask.FromResult(default(ResourceAssetLease<T>));

        /// <summary>
        /// 异步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadLeaseAsync<T>(location, cancellationToken, packageName) ?? UniTask.FromResult(default(ResourceAssetLease<T>));

        /// <summary>
        /// 尝试从资源租约中读取 Unity 资源对象。
        /// </summary>
        public static bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UnityEngine.Object asset)
        {
            if (s_Handler != null)
            {
                return s_Handler.TryGetLeaseAsset(handle, out asset);
            }

            asset = null;
            return false;
        }

        #endregion

        #region 遗留 API [LEGACY API]

        /// <summary>
        /// 卸载资源。
        /// </summary>
        [Obsolete("Use ResourceAssetLease<T> or Binding instead of LoadAsset/UnloadAsset.")]
        public static void UnloadAsset(object asset) => s_Handler?.UnloadAsset(asset);

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）。
        /// </summary>
        public static void UnloadUnusedAssets() => s_Handler?.UnloadUnusedAssets();

        /// <summary>
        /// 资源回收。
        /// </summary>
        public static void UnloadUnusedAssets(bool force) => s_Handler?.UnloadUnusedAssets(force);

        /// <summary>
        /// 强制回收所有资源。
        /// </summary>
        public static void ForceUnloadAllAssets() => s_Handler?.ForceUnloadAllAssets();

        /// <summary>
        /// 强制执行释放未被使用的资源。
        /// </summary>
        public static void ForceUnloadUnusedAssets(bool performGCCollect) => s_Handler?.ForceUnloadUnusedAssets(performGCCollect);

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        public static EHasAssetResult HasAsset(string location, string packageName = "") =>
            s_Handler?.HasAsset(location, packageName) ?? EHasAssetResult.InvalidLocation;

        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        public static bool IsLocationValid(string location, string packageName = "") =>
            s_Handler?.IsLocationValid(location, packageName) ?? false;

        /// <summary>
        /// 检查资源是否需要从远端下载。
        /// </summary>
        public static bool IsNeedDownloadFromRemote(string location, string packageName = "") =>
            s_Handler?.IsNeedDownloadFromRemote(location, packageName) ?? false;

        /// <summary>
        /// 检查资源是否需要从远端下载。
        /// </summary>
        public static bool IsNeedDownloadFromRemote(AssetInfo assetInfo, string packageName = "") =>
            s_Handler?.IsNeedDownloadFromRemote(assetInfo, packageName) ?? false;

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        public static AssetInfo[] GetAssetInfos(string resTag, string packageName = "") =>
            s_Handler?.GetAssetInfos(resTag, packageName);

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        public static AssetInfo[] GetAssetInfos(string[] tags, string packageName = "") =>
            s_Handler?.GetAssetInfos(tags, packageName);

        /// <summary>
        /// 获取资源信息。
        /// </summary>
        public static AssetInfo GetAssetInfo(string location, string packageName = "") =>
            s_Handler?.GetAssetInfo(location, packageName);

        /// <summary>
        /// 每帧过期处理（由 <see cref="ResourceServiceDriver"/> 驱动）。
        /// </summary>
        public static void ProcessKeepAlive(float time, int processCount) =>
            s_Handler?.ProcessKeepAlive(time, processCount);

        /// <summary>
        /// 低内存行为。
        /// </summary>
        public static void OnLowMemory() => s_Handler?.OnLowMemory();

        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        public static void SetForceUnloadUnusedAssetsAction(Action<bool> action) =>
            s_Handler?.SetForceUnloadUnusedAssetsAction(action);

        #endregion

        #region 遗留资源加载 [LEGACY ASSET LOADING]

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static void LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "") =>
            s_Handler?.LoadAssetAsync(location, priority, loadAssetCallbacks, userData, packageName);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static void LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "") =>
            s_Handler?.LoadAssetAsync(location, assetType, priority, loadAssetCallbacks, userData, packageName);

        /// <summary>
        /// 同步加载资源。
        /// </summary>
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        public static T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadAsset<T>(location, packageName);

        /// <summary>
        /// 同步加载资源。
        /// </summary>
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        public static UnityEngine.Object LoadAsset(string location, Type assetType, string packageName = "") =>
            s_Handler?.LoadAsset(location, assetType, packageName);

        /// <summary>
        /// 同步加载游戏物体并实例化。
        /// </summary>
        public static GameObject LoadGameObject(string location, Transform parent = null, string packageName = "") =>
            s_Handler?.LoadGameObject(location, parent, packageName);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTaskVoid LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object
        {
            if (s_Handler != null)
            {
                return s_Handler.LoadAsset(location, callback, packageName);
            }

            callback?.Invoke(null);
            return default;
        }

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadAssetAsync<T>(location, cancellationToken, packageName) ?? UniTask.FromResult<T>(null);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTask<UnityEngine.Object> LoadAssetAsync(string location, Type assetType, CancellationToken cancellationToken = default, string packageName = "") =>
            s_Handler?.LoadAssetAsync(location, assetType, cancellationToken, packageName) ?? UniTask.FromResult<UnityEngine.Object>(null);

        /// <summary>
        /// 异步加载游戏物体并实例化。
        /// </summary>
        public static UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "") =>
            s_Handler?.LoadGameObjectAsync(location, parent, cancellationToken, packageName) ?? UniTask.FromResult<GameObject>(null);

        #endregion

        #region 句柄获取 [HANDLE ACCESS]

        /// <summary>
        /// 获取同步加载的资源操作句柄（逃生舱 API）。
        /// </summary>
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public static AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadAssetSyncHandle<T>(location, packageName);

        /// <summary>
        /// 获取同步加载的资源操作句柄（逃生舱 API）。
        /// </summary>
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public static AssetHandle LoadAssetSyncHandle(string location, Type assetType, string packageName = "") =>
            s_Handler?.LoadAssetSyncHandle(location, assetType, packageName);

        /// <summary>
        /// 获取异步加载的资源操作句柄（逃生舱 API）。
        /// </summary>
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public static AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadAssetAsyncHandle<T>(location, packageName);

        /// <summary>
        /// 获取异步加载的资源操作句柄（逃生舱 API）。
        /// </summary>
        [Obsolete("Prefer LoadLease<T>/LoadLeaseAsync<T>; this escape hatch leaks YooAsset AssetHandle lifecycle to the caller.")]
        public static AssetHandle LoadAssetAsyncHandle(string location, Type assetType, string packageName = "") =>
            s_Handler?.LoadAssetAsyncHandle(location, assetType, packageName);

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <summary>
        /// 获取当前资源包版本。
        /// </summary>
        public static string GetPackageVersion(string customPackageName = "") =>
            s_Handler?.GetPackageVersion(customPackageName);

        /// <summary>
        /// 异步更新最新包的版本。
        /// </summary>
        public static RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "") =>
            s_Handler?.RequestPackageVersionAsync(appendTimeTicks, timeout, customPackageName);

        /// <summary>
        /// 设置远程服务 URL。
        /// </summary>
        public static void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer) =>
            s_Handler?.SetRemoteServicesUrl(defaultHostServer, fallbackHostServer);

        /// <summary>
        /// 向网络端请求并更新清单。
        /// </summary>
        public static LoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "") =>
            s_Handler?.LoadPackageManifestAsync(packageVersion, timeout, customPackageName);

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        public static ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "") =>
            s_Handler?.CreateResourceDownloader(customPackageName);

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        public static ClearCacheOperation ClearCacheAsync(ClearCacheOptions options, string customPackageName = "") =>
            s_Handler?.ClearCacheAsync(options, customPackageName);

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        public static void ClearAllBundleFiles(string customPackageName = "") =>
            s_Handler?.ClearAllBundleFiles(customPackageName);

        #endregion
    }
}
