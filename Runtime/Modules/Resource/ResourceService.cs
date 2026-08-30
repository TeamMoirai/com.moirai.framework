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
        #region 处理器 [HANDLER]

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

        #endregion

        #region 生命周期 [LIFECYCLE]

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
            Application.lowMemory -= OnLowMemory;

            DriveTeardown();
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
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
            get => Handler.DefaultPackageName;
            set { Handler.DefaultPackageName = value; }
        }

        /// <summary>
        /// 运行模式。
        /// </summary>
        public static EResourcePlayMode PlayMode
        {
            get => Handler.PlayMode;
            set { Handler.PlayMode = value; }
        }

        /// <summary>
        /// 资源绑定服务。
        /// </summary>
        public static IResourceBindingService BindingService => Handler.BindingService;

        /// <summary>
        /// 热更 URL，资源服务器地址。
        /// </summary>
        public static string HostServerURL
        {
            get => Handler.HostServerURL;
            set { Handler.HostServerURL = value; }
        }

        /// <summary>
        /// 备用热更 URL。
        /// </summary>
        public static string FallbackHostServerURL
        {
            get => Handler.FallbackHostServerURL;
            set { Handler.FallbackHostServerURL = value; }
        }

        /// <summary>
        /// WebGL 平台加载本地资源/加载远程资源。
        /// </summary>
        public static EResourceLoadWayWebGL LoadResWayWebGL
        {
            get => Handler.LoadResWayWebGL;
            set { Handler.LoadResWayWebGL = value; }
        }

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public static string ApplicableGameVersion => Handler.ApplicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public static int InternalResourceVersion => Handler.InternalResourceVersion;

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        public static string PackageVersion
        {
            get => Handler.PackageVersion;
            set { Handler.PackageVersion = value; }
        }

        /// <summary>
        /// 是否边玩边下载。
        /// </summary>
        public static bool UpdatableWhilePlaying => Handler.UpdatableWhilePlaying;

        #endregion

        #region 运行时配置 [RUNTIME CONFIGURATION]

        /// <summary>
        /// 自动释放资源引用计数为 0 的资源包。
        /// </summary>
        public static bool AutoUnloadBundleWhenUnused
        {
            get => Handler.AutoUnloadBundleWhenUnused;
            set { Handler.AutoUnloadBundleWhenUnused = value; }
        }

        /// <summary>
        /// 同时下载的最大数目。
        /// </summary>
        public static int DownloadingMaxNum
        {
            get => Handler.DownloadingMaxNum;
            set { Handler.DownloadingMaxNum = value; }
        }

        /// <summary>
        /// 下载失败重试次数。
        /// </summary>
        public static int FailedTryAgain
        {
            get => Handler.FailedTryAgain;
            set { Handler.FailedTryAgain = value; }
        }

        /// <summary>
        /// 异步系统每帧执行消耗的最大时间切片（单位：毫秒）。
        /// </summary>
        public static long Milliseconds
        {
            get => Handler.Milliseconds;
            set { Handler.Milliseconds = value; }
        }

        #endregion

        #region 容量与过期 [CAPACITY & EXPIRY]

        /// <summary>
        /// 资源记录预热容量。
        /// </summary>
        public static int AssetRecordCapacity
        {
            get => Handler.AssetRecordCapacity;
            set { Handler.AssetRecordCapacity = value; }
        }

        /// <summary>
        /// 资源租约预热容量。
        /// </summary>
        public static int AssetLeaseCapacity
        {
            get => Handler.AssetLeaseCapacity;
            set { Handler.AssetLeaseCapacity = value; }
        }

        /// <summary>
        /// 绑定所有者预热容量。
        /// </summary>
        public static int BindingOwnerCapacity
        {
            get => Handler.BindingOwnerCapacity;
            set { Handler.BindingOwnerCapacity = value; }
        }

        /// <summary>
        /// 绑定槽位预热容量。
        /// </summary>
        public static int BindingSlotCapacity
        {
            get => Handler.BindingSlotCapacity;
            set { Handler.BindingSlotCapacity = value; }
        }

        /// <summary>
        /// 已注册目标预热容量。
        /// </summary>
        public static int RegisteredTargetCapacity
        {
            get => Handler.RegisteredTargetCapacity;
            set { Handler.RegisteredTargetCapacity = value; }
        }

        /// <summary>
        /// 无引用资源句柄进入 Idle 后的过期秒数。
        /// </summary>
        public static float IdleAssetExpireTime
        {
            get => Handler.IdleAssetExpireTime;
            set { Handler.IdleAssetExpireTime = value; }
        }

        /// <summary>
        /// 预热资源记录。
        /// </summary>
        public static void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity) =>
            Handler.WarmupResourceRecords(assetCapacity, leaseCapacity, unityObjectIndexCapacity);

        /// <summary>
        /// 批量获取资源信息。
        /// </summary>
        public static int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount) =>
            Handler.GetAssetInfos(results, startIndex, maxCount);

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化资源包。
        /// </summary>
        public static UniTask<ResourcePackageInitResult> InitPackage(string customPackageName, bool needInitManifest = false) =>
            Handler.InitPackage(customPackageName, needInitManifest);

        /// <summary>
        /// 初始化资源包（仅初始化包，不更新清单），并发去重与幂等语义与 <see cref="InitPackage"/> 一致。
        /// </summary>
        /// <param name="packageName">资源包名称。为空时使用默认资源包。</param>
        /// <param name="hostServerURL">资源服务器地址。非空时写入 <see cref="HostServerURL"/>。</param>
        /// <param name="fallbackHostServerURL">备用资源服务器地址。非空时写入 <see cref="FallbackHostServerURL"/>。</param>
        /// <returns>初始化是否成功。</returns>
        public static UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "") =>
            Handler.InitPackageAsync(packageName, hostServerURL, fallbackHostServerURL);

        #endregion

        #region 租约 API [LEASE API]

        /// <summary>
        /// 使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        public static ResourceLeaseHandle AcquireDirect(ResourceKey key) =>
            Handler.AcquireDirect(key);

        /// <summary>
        /// 异步获取一个直接资源租约。
        /// </summary>
        public static UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default) =>
            Handler.AcquireDirectAsync(key, cancellationToken);

        /// <summary>
        /// 尝试使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        public static bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle)
        {
            return Handler.TryAcquireDirect(key, out handle);
        }

        /// <summary>
        /// 释放一个显式资源租约。
        /// </summary>
        public static void Release(ResourceLeaseHandle handle) => Handler.Release(handle);

        /// <summary>
        /// 同步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : UnityEngine.Object =>
            Handler.LoadLease<T>(key);

        /// <summary>
        /// 同步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : UnityEngine.Object =>
            Handler.LoadLease<T>(location, packageName);

        /// <summary>
        /// 异步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default) where T : UnityEngine.Object =>
            Handler.LoadLeaseAsync<T>(key, cancellationToken);

        /// <summary>
        /// 异步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        public static UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object =>
            Handler.LoadLeaseAsync<T>(location, cancellationToken, packageName);

        /// <summary>
        /// 尝试从资源租约中读取 Unity 资源对象。
        /// </summary>
        public static bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UnityEngine.Object asset)
        {
            return Handler.TryGetLeaseAsset(handle, out asset);
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
            Handler.LoadAsset<T>(location, packageName);

        /// <summary>
        /// 异步加载资源。每次成功回调资源后，调用方必须在不再使用时成对调用 <see cref="UnloadAsset"/>。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="callback">回调函数。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public static UniTask LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object =>
            Handler.LoadAsset(location, callback, packageName);

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
            Handler.LoadAssetAsync<T>(location, cancellationToken, packageName);

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
            Handler.LoadAssetAsync(location, assetType, priority, loadAssetCallbacks, userData, packageName);

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
            Handler.LoadAssetAsync(location, priority, loadAssetCallbacks, userData, packageName);

        /// <summary>
        /// 卸载资源。
        /// </summary>
        /// <param name="asset">要卸载的资源。每次成功调用直接返回资源的 LoadAsset 接口后，都需要成对调用一次。</param>
        [Obsolete("Use ResourceAssetLease<T> or Binding instead of LoadAsset/UnloadAsset.")]
        public static void UnloadAsset(object asset) => Handler.UnloadAsset(asset);

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）。
        /// </summary>
        public static void UnloadUnusedAssets() => Handler.UnloadUnusedAssets();

        /// <summary>
        /// 资源回收。
        /// </summary>
        public static void UnloadUnusedAssets(bool force) => Handler.UnloadUnusedAssets(force);

        /// <summary>
        /// 强制回收所有资源。
        /// </summary>
        public static void ForceUnloadAllAssets() => Handler.ForceUnloadAllAssets();

        /// <summary>
        /// 强制执行释放未被使用的资源。
        /// </summary>
        public static void ForceUnloadUnusedAssets(bool performGCCollect) => Handler.ForceUnloadUnusedAssets(performGCCollect);

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        public static EResourceHasAssetResult HasAsset(string location, string packageName = "") =>
            Handler.HasAsset(location, packageName);

        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        public static bool IsLocationValid(string location, string packageName = "") =>
            Handler.IsLocationValid(location, packageName);

        /// <summary>
        /// 检查资源是否需要从远端下载。
        /// </summary>
        public static bool IsNeedDownloadFromRemote(string location, string packageName = "") =>
            Handler.IsNeedDownloadFromRemote(location, packageName);

        /// <summary>
        /// 获取资源需要从远端下载的字节数。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <returns>待下载字节数；服务不可用时返回 -1。</returns>
        public static long GetDownloadSize(string location, string packageName = "") =>
            Handler.GetDownloadSize(location, packageName);

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        public static ResourceAssetInfoEntry[] GetAssetInfos(string resTag, string packageName = "") =>
            Handler.GetAssetInfos(resTag, packageName);

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        public static ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "") =>
            Handler.GetAssetInfos(tags, packageName);

        /// <summary>
        /// 获取资源信息。
        /// </summary>
        public static ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "") =>
            Handler.GetAssetInfo(location, packageName);

        /// <summary>
        /// 每帧过期处理。
        /// </summary>
        internal static void ProcessKeepAlive(float time, int processCount) =>
            Handler.ProcessKeepAlive(time, processCount);

        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        public static void SetForceUnloadUnusedAssetsAction(Action<bool> action) =>
            Handler.SetForceUnloadUnusedAssetsAction(action);

        #endregion

        #region 资源加载 [ASSET LOADING]

        /// <summary>
        /// 同步加载游戏物体并实例化。
        /// </summary>
        public static GameObject LoadGameObject(string location, Transform parent = null, string packageName = "") =>
            Handler.LoadGameObject(location, parent, packageName);

        /// <summary>
        /// 异步加载游戏物体并实例化。
        /// </summary>
        public static UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "") =>
            Handler.LoadGameObjectAsync(location, parent, cancellationToken, packageName);

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <summary>
        /// 获取当前资源包版本。
        /// </summary>
        public static string GetPackageVersion(string customPackageName = "") =>
            Handler.GetPackageVersion(customPackageName);

        /// <summary>
        /// 异步更新最新包的版本。
        /// </summary>
        public static ResourcePackageVersionResult RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "") =>
            Handler.RequestPackageVersionAsync(appendTimeTicks, timeout, customPackageName);

        /// <summary>
        /// 设置远程服务 URL。
        /// </summary>
        public static void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer) =>
            Handler.SetRemoteServicesUrl(defaultHostServer, fallbackHostServer);

        /// <summary>
        /// 向网络端请求并更新清单。
        /// </summary>
        public static IResourceOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "") =>
            Handler.LoadPackageManifestAsync(packageVersion, timeout, customPackageName);

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        public static IResourceDownloader CreateResourceDownloader(string customPackageName = "") =>
            Handler.CreateResourceDownloader(customPackageName);

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        public static ResourceClearCacheResult ClearCacheAsync(EResourceClearMode clearMode, string customPackageName = "") =>
            Handler.ClearCacheAsync(clearMode, customPackageName);

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        public static void ClearAllBundleFiles(string customPackageName = "") =>
            Handler.ClearAllBundleFiles(customPackageName);

        #endregion
    }
}
