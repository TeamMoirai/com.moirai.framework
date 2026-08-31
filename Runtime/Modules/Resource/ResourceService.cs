using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源管理器外观（Facade），为游戏提供统一的资源加载、缓存、租约与绑定接口。
    /// <para>统一的静态资源访问入口，通过替换 <see cref="Handler"/> 即可在不同资源后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="ResourceServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(ResourceServiceHandler))]
    public partial class ResourceService : ServiceBase
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="ResourceServiceSettings"/> 配置创建默认资源处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认资源处理器实例。</returns>
        private static ResourceServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<ResourceService>();
            return ResourceServiceSettings.ResourceServiceHandlerConfig.CreateHandler();
        }

        /// <inheritdoc />
        public override int Priority => 4;

        /// <summary>
        /// 初始化资源服务。由容器在构建期调用：触发 <see cref="Handler"/> 懒加载、
        /// 注入配置并接线帧驱动（时间轮推进/卸载调度/GC 节流/低内存响应）。
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;

            DriveInitialize();
            Application.lowMemory += OnLowMemory;
        }

        /// <summary>
        /// 关闭资源服务。由容器在关闭期调用：先解除驱动接线，再关闭处理器。
        /// </summary>
        public override void OnShutdown()
        {
            DriveTeardown();

            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();

            Application.lowMemory -= OnLowMemory;
        }

        /// <summary>
        /// 低内存响应转发。
        /// </summary>
        private static void OnLowMemory()
        {
            LogUtility.Warning("[LowMemory] Unload Unused Assets...");
            s_Handler?.ForceUnloadUnusedAssets(true);
        }

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
            set
            {
                if (s_Handler == null) return;
                s_Handler.DefaultPackageName = value;
            }
        }

        /// <summary>
        /// 运行模式。
        /// </summary>
        public static EResourcePlayMode PlayMode
        {
            get => s_Handler?.PlayMode ?? EResourcePlayMode.Offline;
            set
            {
                if (s_Handler == null) return;
                s_Handler.PlayMode = value;
            }
        }

        /// <summary>
        /// 资源绑定服务。
        /// </summary>
        public static IResourceBindingService BindingService => s_Handler?.BindingService;

        /// <summary>
        /// 热更 URL，资源服务器地址。
        /// </summary>
        public static string HostServerURL
        {
            get => s_Handler?.HostServerURL;
            set
            {
                if (s_Handler == null) return;
                s_Handler.HostServerURL = value;
            }
        }

        /// <summary>
        /// 备用热更 URL。
        /// </summary>
        public static string FallbackHostServerURL
        {
            get => s_Handler?.FallbackHostServerURL;
            set
            {
                if (s_Handler == null) return;
                s_Handler.FallbackHostServerURL = value;
            }
        }

        /// <summary>
        /// WebGL 平台加载本地资源/加载远程资源。
        /// </summary>
        public static EResourceLoadWayWebGL LoadResWayWebGL
        {
            get => s_Handler?.LoadResWayWebGL ?? EResourceLoadWayWebGL.Undefined;
            set
            {
                if (s_Handler == null) return;
                s_Handler.LoadResWayWebGL = value;
            }
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
            set
            {
                if (s_Handler == null) return;
                s_Handler.PackageVersion = value;
            }
        }

        /// <summary>
        /// 是否边玩边下载。
        /// </summary>
        public static bool UpdatableWhilePlaying => s_Handler?.UpdatableWhilePlaying ?? false;

        #endregion

        #region 运行时配置 [RUNTIME CONFIGURATION]

        /// <summary>
        /// 自动释放资源引用计数为 0 的资源包。
        /// </summary>
        public static bool AutoUnloadBundleWhenUnused
        {
            get => s_Handler?.AutoUnloadBundleWhenUnused ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.AutoUnloadBundleWhenUnused = value;
            }
        }

        /// <summary>
        /// 同时下载的最大数目。
        /// </summary>
        public static int DownloadingMaxNum
        {
            get => s_Handler?.DownloadingMaxNum ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.DownloadingMaxNum = value;
            }
        }

        /// <summary>
        /// 下载失败重试次数。
        /// </summary>
        public static int FailedTryAgain
        {
            get => s_Handler?.FailedTryAgain ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.FailedTryAgain = value;

            }
        }

        /// <summary>
        /// 异步系统每帧执行消耗的最大时间切片（单位：毫秒）。
        /// </summary>
        public static long Milliseconds
        {
            get => s_Handler?.Milliseconds ?? 0L;
            set
            {
                if (s_Handler == null) return;
                s_Handler.Milliseconds = value;
            }
        }

        #endregion

        #region 容量与过期 [CAPACITY & EXPIRY]

        /// <summary>
        /// 资源记录预热容量。
        /// </summary>
        public static int AssetRecordCapacity
        {
            get => s_Handler?.AssetRecordCapacity ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.AssetRecordCapacity = value;
            }
        }

        /// <summary>
        /// 资源租约预热容量。
        /// </summary>
        public static int AssetLeaseCapacity
        {
            get => s_Handler?.AssetLeaseCapacity ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.AssetLeaseCapacity = value;
            }
        }

        /// <summary>
        /// 绑定所有者预热容量。
        /// </summary>
        public static int BindingOwnerCapacity
        {
            get => s_Handler?.BindingOwnerCapacity ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.BindingOwnerCapacity = value;
            }
        }

        /// <summary>
        /// 绑定槽位预热容量。
        /// </summary>
        public static int BindingSlotCapacity
        {
            get => s_Handler?.BindingSlotCapacity ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.BindingSlotCapacity = value;
            }
        }

        /// <summary>
        /// 已注册目标预热容量。
        /// </summary>
        public static int RegisteredTargetCapacity
        {
            get => s_Handler?.RegisteredTargetCapacity ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.RegisteredTargetCapacity = value;
            }
        }

        /// <summary>
        /// 无引用资源句柄进入 Idle 后的过期秒数。
        /// </summary>
        public static float IdleAssetExpireTime
        {
            get => s_Handler?.IdleAssetExpireTime ?? 0;
            set
            {
                if (s_Handler == null) return;
                s_Handler.IdleAssetExpireTime = value;
            }
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
        /// 初始化资源包。
        /// </summary>
        public static UniTask<ResourcePackageInitResult> InitPackage(string customPackageName, bool needInitManifest = false) =>
            s_Handler?.InitPackage(customPackageName, needInitManifest) ?? UniTask.FromResult<ResourcePackageInitResult>(null);

        /// <summary>
        /// 初始化资源包（仅初始化包，不更新清单），并发去重与幂等语义与 <see cref="InitPackage"/> 一致。
        /// </summary>
        /// <param name="packageName">资源包名称。为空时使用默认资源包。</param>
        /// <param name="hostServerURL">资源服务器地址。非空时写入 <see cref="HostServerURL"/>。</param>
        /// <param name="fallbackHostServerURL">备用资源服务器地址。非空时写入 <see cref="FallbackHostServerURL"/>。</param>
        /// <returns>初始化是否成功。</returns>
        public static UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "") =>
            s_Handler?.InitPackageAsync(packageName, hostServerURL, fallbackHostServerURL) ?? UniTask.FromResult<bool>(false);

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
            if (s_Handler == null)
            {
                handle = ResourceLeaseHandle.Invalid;
                return false;
            }

            return s_Handler.TryAcquireDirect(key, out handle);
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
            s_Handler?.LoadLeaseAsync<T>(key, cancellationToken) ?? UniTask.FromResult<ResourceAssetLease<T>>(default);

        /// <summary>
        /// 异步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadLeaseAsync<T>(location, cancellationToken, packageName) ?? UniTask.FromResult<ResourceAssetLease<T>>(default);

        /// <summary>
        /// 尝试从资源租约中读取 Unity 资源对象。
        /// </summary>
        public static bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UnityEngine.Object asset)
        {
            if (s_Handler == null)
            {
                asset = null;
                return false;
            }

            return s_Handler.TryGetLeaseAsset(handle, out asset);
        }

        #endregion

        #region 遗留 API [LEGACY API]

        /// <summary>
        /// 同步加载资源。每次成功调用后，调用方必须在不再使用时成对调用 <see cref="UnloadAsset"/>。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        /// <returns>资源实例。</returns>
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        public static T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadAsset<T>(location, packageName);

        /// <summary>
        /// 异步加载资源。每次成功回调资源后，调用方必须在不再使用时成对调用 <see cref="UnloadAsset"/>。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="callback">回调函数。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTask LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadAsset(location, callback, packageName) ?? UniTask.CompletedTask;

        /// <summary>
        /// 异步加载资源。每次成功返回资源后，调用方必须在不再使用时成对调用 <see cref="UnloadAsset"/>。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消操作 Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        /// <returns>异步资源实例。</returns>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object =>
            s_Handler?.LoadAssetAsync<T>(location, cancellationToken, packageName) ?? UniTask.FromResult<T>(null);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="assetType">要加载的资源类型。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTask LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "") =>
            s_Handler?.LoadAssetAsync(location, assetType, priority, loadAssetCallbacks, userData, packageName) ?? UniTask.CompletedTask;

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTask LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "") =>
            s_Handler?.LoadAssetAsync(location, priority, loadAssetCallbacks, userData, packageName) ?? UniTask.CompletedTask;

        /// <summary>
        /// 卸载资源。
        /// </summary>
        /// <param name="asset">要卸载的资源。每次成功调用直接返回资源的 LoadAsset 接口后，都需要成对调用一次。</param>
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
        public static EResourceHasAssetResult HasAsset(string location, string packageName = "") =>
            s_Handler?.HasAsset(location, packageName) ?? EResourceHasAssetResult.NotExist;

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
        /// 获取资源需要从远端下载的字节数。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <returns>待下载字节数；服务不可用时返回 -1。</returns>
        public static long GetDownloadSize(string location, string packageName = "") =>
            s_Handler?.GetDownloadSize(location, packageName) ?? -1L;

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        public static ResourceAssetInfoEntry[] GetAssetInfos(string resTag, string packageName = "") =>
            s_Handler?.GetAssetInfos(resTag, packageName);

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        public static ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "") =>
            s_Handler?.GetAssetInfos(tags, packageName);

        /// <summary>
        /// 获取资源信息。
        /// </summary>
        public static ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "") =>
            s_Handler?.GetAssetInfo(location, packageName) ?? default;

        /// <summary>
        /// 每帧过期处理。
        /// </summary>
        internal static void ProcessKeepAlive(float time, int processCount) =>
            s_Handler?.ProcessKeepAlive(time, processCount);

        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        public static void SetForceUnloadUnusedAssetsAction(Action<bool> action) =>
            s_Handler?.SetForceUnloadUnusedAssetsAction(action);

        #endregion

        #region 资源加载 [ASSET LOADING]

        /// <summary>
        /// 同步加载游戏物体并实例化。
        /// </summary>
        public static GameObject LoadGameObject(string location, Transform parent = null, string packageName = "") =>
            s_Handler?.LoadGameObject(location, parent, packageName);

        /// <summary>
        /// 异步加载游戏物体并实例化。
        /// </summary>
        public static UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "") =>
            s_Handler?.LoadGameObjectAsync(location, parent, cancellationToken, packageName) ?? UniTask.FromResult<GameObject>(null);

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
        public static ResourcePackageVersionResult RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "") =>
            s_Handler?.RequestPackageVersionAsync(appendTimeTicks, timeout, customPackageName);

        /// <summary>
        /// 设置远程服务 URL。
        /// </summary>
        public static void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer) =>
            s_Handler?.SetRemoteServicesUrl(defaultHostServer, fallbackHostServer);

        /// <summary>
        /// 向网络端请求并更新清单。
        /// </summary>
        public static IResourceOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "") =>
            s_Handler?.LoadPackageManifestAsync(packageVersion, timeout, customPackageName);

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        public static IResourceDownloader CreateResourceDownloader(string customPackageName = "") =>
            s_Handler?.CreateResourceDownloader(customPackageName);

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        public static ResourceClearCacheResult ClearCacheAsync(EResourceClearMode clearMode, string customPackageName = "") =>
            s_Handler?.ClearCacheAsync(clearMode, customPackageName);

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        public static void ClearAllBundleFiles(string customPackageName = "") =>
            s_Handler?.ClearAllBundleFiles(customPackageName);

        #endregion
    }
}
