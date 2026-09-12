using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Debugger;
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
    [ServiceDependency(typeof(DebuggerService))]
    public partial class ResourceService : ServiceBase, IServiceTickable
    {
        #region 驱动状态 [DRIVE STATE]

        private static bool s_ForceUnloadUnusedAssets;
        private static bool s_ForceSystemUnloadUnusedAssets;
        private static bool s_PreorderUnloadUnusedAssets;
        private static bool s_PerformGCCollect;

        private static AsyncOperation s_AsyncOperation;
        private static float s_LastUnloadElapsedSeconds;
        private static float s_LastGCCollectElapsedSeconds = float.MaxValue;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="ResourceServiceSettings"/> 创建默认资源处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认资源处理器实例。</returns>
        private static ResourceServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<ResourceService>();
            return ResourceServiceSettings.ResourceServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.RESOURCE;

        /// <summary>
        /// 初始化资源服务。由容器在构建期调用：触发 <see cref="Handler"/> 懒加载、
        /// 注入配置并接线帧驱动（时间轮推进/卸载调度/GC 节流/低内存响应）。
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;

            // 远程地址（更新系统单源）
            s_Handler.HostServerURL = UpdateSettings.GetResDownLoadPath();
            s_Handler.FallbackHostServerURL = UpdateSettings.GetFallbackResDownLoadPath();
            s_Handler.LoadResWayWebGL = (EResourceLoadWayWebGL)UpdateSettings.LoadResWayWebGL;

            // 通用配置（ResourceSettings 单源）
            s_Handler.AssetRecordCapacity = ResourceServiceSettings.AssetRecordCapacity;
            s_Handler.AssetLeaseCapacity = ResourceServiceSettings.AssetLeaseCapacity;
            s_Handler.BindingOwnerCapacity = ResourceServiceSettings.BindingOwnerCapacity;
            s_Handler.BindingSlotCapacity = ResourceServiceSettings.BindingSlotCapacity;
            s_Handler.RegisteredTargetCapacity = ResourceServiceSettings.RegisteredTargetCapacity;
            s_Handler.IdleAssetExpireTime = ResourceServiceSettings.IdleAssetExpireTime;
            s_Handler.SetForceUnloadUnusedAssetsAction(RequestForceUnloadUnusedAssets);

            // 初始化后端（创建默认包与绑定服务）
            s_Handler.Initialize();
            LogUtility.Info("ResourceService Run Mode：{0}", ResourceServiceSettings.PlayMode);

            Application.lowMemory += OnLowMemory;

            DebuggerService.RegisterDebuggerWindow("Profiler/Resource", new ResourceServiceDebuggerWindow());
        }

        /// <summary>
        /// 关闭资源服务。由容器在关闭期调用：先解除驱动接线，再关闭处理器。
        /// </summary>
        public override void OnShutdown()
        {
            Application.lowMemory -= OnLowMemory;

            s_AsyncOperation = null;
            s_ForceUnloadUnusedAssets = false;
            s_ForceSystemUnloadUnusedAssets = false;
            s_PreorderUnloadUnusedAssets = false;
            s_PerformGCCollect = false;
            s_LastUnloadElapsedSeconds = 0f;
            s_LastGCCollectElapsedSeconds = float.MaxValue;

            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 每帧驱动：时间轮推进 + 无用资源卸载调度 + GC 节流。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (s_Handler == null) return;

            float minInterval = ResourceServiceSettings.MinUnloadUnusedAssetsInterval;
            float maxInterval = ResourceServiceSettings.MaxUnloadUnusedAssetsInterval;
            bool useSystem = ResourceServiceSettings.UseSystemUnloadUnusedAssets;
            int expirePerFrame = ResourceServiceSettings.ExpireProcessCountPerFrame;
            int expireWhenUnloading = ResourceServiceSettings.ExpireProcessCountWhenUnloading;
            float minGCInterval = ResourceServiceSettings.MinGCCollectInterval;

            bool operationInFlight = s_AsyncOperation != null;
            bool shouldUnloadUnusedAssets = ShouldUnloadUnusedAssets(
                operationInFlight,
                s_LastUnloadElapsedSeconds,
                s_ForceUnloadUnusedAssets,
                s_PreorderUnloadUnusedAssets,
                minInterval,
                maxInterval);

            int expireProcessCount = ResolveExpireProcessCount(shouldUnloadUnusedAssets, expirePerFrame, expireWhenUnloading);
            s_Handler.ProcessKeepAlive(Time.unscaledTime, expireProcessCount);

            s_LastUnloadElapsedSeconds += Time.unscaledDeltaTime;
            s_LastGCCollectElapsedSeconds += Time.unscaledDeltaTime;
            if (shouldUnloadUnusedAssets)
            {
                bool force = s_ForceUnloadUnusedAssets;
                bool useSystemUnload = s_ForceSystemUnloadUnusedAssets && useSystem;
                s_ForceUnloadUnusedAssets = false;
                s_ForceSystemUnloadUnusedAssets = false;
                s_PreorderUnloadUnusedAssets = false;
                s_LastUnloadElapsedSeconds = 0f;
                s_Handler.UnloadUnusedAssets(force);
                s_AsyncOperation = useSystemUnload ? Resources.UnloadUnusedAssets() : null;
                LogUtility.Info("Unload unused assets...");
            }

            if (s_AsyncOperation == null && s_PerformGCCollect)
            {
                TryCollectGarbage(minGCInterval);
            }

            if (s_AsyncOperation is { isDone: true })
            {
                s_AsyncOperation = null;
                if (s_PerformGCCollect)
                {
                    TryCollectGarbage(minGCInterval);
                }
            }
        }

        #endregion

        #region 属性 [PROPERTIES]
		
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
            get => ResourceServiceSettings.PlayMode;
            set => ResourceServiceSettings.PlayMode = value;
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
            s_Handler?.InitPackageAsync(packageName, hostServerURL, fallbackHostServerURL) ?? UniTask.FromResult(false);

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
        /// 低内存行为。
        /// </summary>
        internal static void OnLowMemory() => s_Handler?.OnLowMemory();

        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        public static void SetForceUnloadUnusedAssetsAction(Action<bool> action) =>
            s_Handler?.SetForceUnloadUnusedAssetsAction(action);

        /// <summary>
        /// 请求强制执行释放未被使用的资源。
        /// </summary>
        /// <param name="performGCCollect">是否使用垃圾回收。</param>
        private static void RequestForceUnloadUnusedAssets(bool performGCCollect)
        {
            s_ForceUnloadUnusedAssets = true;
            if (performGCCollect)
            {
                s_PerformGCCollect = true;
                s_ForceSystemUnloadUnusedAssets = true;
            }
        }

        private static void TryCollectGarbage(float minInterval)
        {
            if (s_LastGCCollectElapsedSeconds < minInterval) return;

            LogUtility.Info("GC.Collect...");
            s_PerformGCCollect = false;
            s_LastGCCollectElapsedSeconds = 0f;
            GC.Collect();
        }

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

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 通过资源系统异步加载场景——场景资源经当前配置的资源后端（YooAsset、Addressable 等）管线加载。
        /// <para>由 <see cref="Scene.SceneService"/> 的默认后端调用，实现场景加载与资源系统的统一。</para>
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载（加载至待激活状态后保持挂起）。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="packageName">资源包名称。为空时使用默认资源包。</param>
        /// <returns>场景加载句柄；服务未就绪时返回 null。</returns>
        public static ResourceSceneHandle LoadSceneAsync(string location, UnityEngine.SceneManagement.LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "") =>
            s_Handler?.LoadSceneAsync(location, sceneMode, suspendLoad, priority, packageName);

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

        #region 调度决策（纯函数，供回归测试）[SCHEDULING DECISIONS]

        /// <summary>
        /// 判定本帧是否应触发无用资源卸载。
        /// </summary>
        /// <param name="operationInFlight">是否已有卸载操作在途。</param>
        /// <param name="elapsedSinceLastUnload">距上次卸载的经过秒数。</param>
        /// <param name="forceRequested">是否被强制请求。</param>
        /// <param name="preorderRequested">是否被预约请求（低优先级提前卸载）。</param>
        /// <param name="minInterval">预约请求生效所需的最小间隔。</param>
        /// <param name="maxInterval">无请求时的最大间隔。</param>
        /// <returns>是否应触发卸载。</returns>
        internal static bool ShouldUnloadUnusedAssets(bool operationInFlight, float elapsedSinceLastUnload,
            bool forceRequested, bool preorderRequested, float minInterval, float maxInterval)
        {
            return !operationInFlight &&
                   (forceRequested ||
                    elapsedSinceLastUnload >= maxInterval ||
                    preorderRequested && elapsedSinceLastUnload >= minInterval);
        }

        /// <summary>
        /// 计算本帧过期处理预算：常态按每帧配额，进入卸载帧时提升至上限且不低于常态值。
        /// </summary>
        internal static int ResolveExpireProcessCount(bool shouldUnload, int perFrameCount, int whenUnloadingCount)
        {
            return Mathf.Max(shouldUnload ? whenUnloadingCount : 0, perFrameCount);
        }

        #endregion
    }
}
