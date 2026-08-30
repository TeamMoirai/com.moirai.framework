using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源管理器处理器抽象基类（策略模式抽象策略）——定义通用资源加载、缓存、租约与绑定契约。
    /// <para>框架通用，不依赖具体资源系统（YooAsset、Addressable 等）；
    /// 由具体后端（如 <see cref="YooAssetHandler"/>、<see cref="AddressableHandler"/>）实现。</para>
    /// <para>由 <see cref="ResourceServiceSettings"/> 序列化配置，<see cref="ResourceService"/> 外观转发调用。</para>
    /// </summary>
    [Serializable]
    public abstract class ResourceServiceHandler : FrameworkHandler
    {
        #region 基础属性 [BASE PROPERTIES]

        /// <summary>
        /// 默认资源包/资源组名称。
        /// </summary>
        public abstract string DefaultPackageName { get; set; }

        /// <summary>
        /// 资源系统运行模式（框架通用）。
        /// </summary>
        public abstract EResourcePlayMode PlayMode { get; set; }

        /// <summary>
        /// 同步初始化回调。默认实现为空，由具体后端覆写以接管资源。
        /// </summary>
        protected override void OnInit()
        {
        }

        /// <summary>
        /// 关闭处理器——释放所有资源记录与在途加载操作。
        /// <para>由 <see cref="ResourceService.OnShutdown"/> 在容器关闭期调用。</para>
        /// </summary>
        protected override void OnShutdown()
        {
        }

        /// <summary>
        /// 绑定服务。
        /// </summary>
        public abstract IResourceBindingService BindingService { get; }

        /// <summary>
        /// 资源系统热更服务器地址。
        /// </summary>
        public abstract string HostServerURL { get; set; }

        /// <summary>
        /// 资源系统备用热更服务器地址。
        /// </summary>
        public abstract string FallbackHostServerURL { get; set; }

        /// <summary>
        /// WebGL 平台加载方式。
        /// </summary>
        public abstract EResourceLoadWayWebGL LoadResWayWebGL { get; set; }

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public abstract string ApplicableGameVersion { get; }

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public abstract int InternalResourceVersion { get; }

        /// <summary>
        /// 当前资源包版本。
        /// </summary>
        public abstract string PackageVersion { get; set; }

        /// <summary>
        /// 是否支持边玩边下载（热更进行中可进入游戏）。
        /// </summary>
        public abstract bool UpdatableWhilePlaying { get; }

        #endregion

        #region 运行时配置 [RUNTIME CONFIGURATION]

        /// <summary>
        /// 自动释放资源引用计数为 0 的资源包。
        /// </summary>
        public abstract bool AutoUnloadBundleWhenUnused { get; set; }

        /// <summary>
        /// 同时下载的最大数目。
        /// </summary>
        public abstract int DownloadingMaxNum { get; set; }

        /// <summary>
        /// 下载失败重试次数。
        /// </summary>
        public abstract int FailedTryAgain { get; set; }

        /// <summary>
        /// 异步系统每帧执行消耗的最大时间切片（单位：毫秒）。
        /// </summary>
        public abstract long Milliseconds { get; set; }

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化资源系统。
        /// </summary>
        public abstract void Initialize();

        /// <summary>
        /// 初始化指定资源包。
        /// </summary>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="needInitManifest">是否需要初始化清单。</param>
        /// <returns>资源包初始化结果。</returns>
        public abstract UniTask<ResourcePackageInitResult> InitPackage(string packageName, bool needInitManifest = false);

        /// <summary>
        /// 初始化指定资源包（仅初始化包，不更新清单），并发去重与幂等语义与 <see cref="InitPackage"/> 一致。
        /// </summary>
        /// <param name="packageName">资源包名称。为空时使用默认资源包。</param>
        /// <param name="hostServerURL">资源服务器地址。非空时写入 <see cref="HostServerURL"/>。</param>
        /// <param name="fallbackHostServerURL">备用资源服务器地址。非空时写入 <see cref="FallbackHostServerURL"/>。</param>
        /// <returns>初始化是否成功。</returns>
        public abstract UniTask<bool> InitPackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "");

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <summary>
        /// 获取指定资源包的版本。
        /// </summary>
        public abstract string GetPackageVersion(string customPackageName = "");

        /// <summary>
        /// 异步请求最新包版本。
        /// </summary>
        public abstract ResourcePackageVersionResult RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "");

        /// <summary>
        /// 设置远程资源服务器地址。
        /// </summary>
        public abstract void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer);

        /// <summary>
        /// 异步加载指定版本的清单。
        /// </summary>
        public abstract IResourceOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "");

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本的所有资源包文件。
        /// </summary>
        public abstract IResourceDownloader CreateResourceDownloader(string customPackageName = "");

        /// <summary>
        /// 清理缓存文件。
        /// </summary>
        public abstract ResourceClearCacheResult ClearCacheAsync(EResourceClearMode clearMode, string customPackageName = "");

        /// <summary>
        /// 清理所有缓存文件（沙盒路径）。
        /// </summary>
        public abstract void ClearAllBundleFiles(string customPackageName = "");

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <summary>
        /// 低内存行为。
        /// </summary>
        public abstract void OnLowMemory();

        /// <summary>
        /// 设置强制卸载未使用资源回调。
        /// </summary>
        public abstract void SetForceUnloadUnusedAssetsAction(Action<bool> action);

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）。
        /// </summary>
        public abstract void UnloadUnusedAssets();

        /// <summary>
        /// 资源回收。
        /// </summary>
        public abstract void UnloadUnusedAssets(bool force);

        /// <summary>
        /// 强制回收所有资源。
        /// </summary>
        public abstract void ForceUnloadAllAssets();

        /// <summary>
        /// 强制执行释放未被使用的资源。
        /// </summary>
        public abstract void ForceUnloadUnusedAssets(bool performGCCollect);

        #endregion

        #region 获取资源信息 [GET ASSET INFOS]

        /// <summary>
        /// 检查资源是否需要从远端下载。
        /// </summary>
        public abstract bool IsNeedDownloadFromRemote(string location, string packageName = "");

        /// <summary>
        /// 获取资源需要从远端下载的字节数。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <returns>待下载字节数；定位地址或资源包无效时抛出 GameException。</returns>
        public abstract long GetDownloadSize(string location, string packageName = "");

        /// <summary>
        /// 按标签获取资源信息列表。
        /// </summary>
        public abstract ResourceAssetInfoEntry[] GetAssetInfos(string tag, string packageName = "");

        /// <summary>
        /// 按标签集合获取资源信息列表。
        /// </summary>
        public abstract ResourceAssetInfoEntry[] GetAssetInfos(string[] tags, string packageName = "");

        /// <summary>
        /// 获取单个资源信息。
        /// </summary>
        public abstract ResourceAssetInfoEntry GetAssetInfo(string location, string packageName = "");

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        public abstract EResourceHasAssetResult HasAsset(string location, string packageName = "");

        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        public abstract bool IsLocationValid(string location, string packageName = "");

        #endregion

        #region 资源加载 [ASSET LOADING]

        /// <summary>
        /// 同步加载游戏物体并实例化。
        /// </summary>
        public abstract GameObject LoadGameObject(string location, Transform parent = null, string packageName = "");

        /// <summary>
        /// 异步加载游戏物体并实例化。
        /// </summary>
        public abstract UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "");

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
        public abstract T LoadAsset<T>(string location, string packageName = "") where T : Object;

        /// <summary>
        /// 异步加载资源。每次成功回调资源后，调用方必须在不再使用时成对调用 <see cref="UnloadAsset"/>。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="callback">回调函数。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public abstract UniTask LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : Object;

        /// <summary>
        /// 异步加载资源。每次成功返回资源后，调用方必须在不再使用时成对调用 <see cref="UnloadAsset"/>。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消操作 Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        /// <returns>异步资源实例。</returns>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public abstract UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : Object;

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
        public abstract UniTask LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "");

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        public abstract UniTask LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "");

        /// <summary>
        /// 卸载资源。
        /// </summary>
        /// <param name="asset">要卸载的资源。每次成功调用直接返回资源的 LoadAsset 接口后，都需要成对调用一次。</param>
        [Obsolete("Use ResourceAssetLease<T> or Binding instead of LoadAsset/UnloadAsset.")]
        public abstract void UnloadAsset(object asset);

        #endregion

        #region 容量属性 [CAPACITY PROPERTIES]

        /// <summary>
        /// 资源记录预热容量。
        /// </summary>
        public abstract int AssetRecordCapacity { get; set; }

        /// <summary>
        /// 资源租约预热容量。
        /// </summary>
        public abstract int AssetLeaseCapacity { get; set; }

        /// <summary>
        /// 绑定所有者预热容量。
        /// </summary>
        public abstract int BindingOwnerCapacity { get; set; }

        /// <summary>
        /// 绑定槽位预热容量。
        /// </summary>
        public abstract int BindingSlotCapacity { get; set; }

        /// <summary>
        /// 已注册目标预热容量。
        /// </summary>
        public abstract int RegisteredTargetCapacity { get; set; }

        /// <summary>
        /// 无引用资源句柄空闲过期秒数。
        /// </summary>
        public abstract float IdleAssetExpireTime { get; set; }

        #endregion

        #region 预热 [WARMUP]

        /// <summary>
        /// 预热资源记录。
        /// </summary>
        public abstract void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity);

        #endregion

        #region 公共 Lease API [PUBLIC LEASE API]

        /// <summary>
        /// 使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        public abstract ResourceLeaseHandle AcquireDirect(ResourceKey key);

        /// <summary>
        /// 异步获取一个直接资源租约。
        /// </summary>
        public abstract UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key, CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        public abstract bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle);

        /// <summary>
        /// 释放一个显式资源租约。
        /// </summary>
        public abstract void Release(ResourceLeaseHandle handle);

        /// <summary>
        /// 同步加载资源并返回资源租约。
        /// </summary>
        public abstract ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : Object;

        /// <summary>
        /// 同步加载资源并返回资源租约。
        /// </summary>
        public abstract ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : Object;

        /// <summary>
        /// 异步加载资源并返回资源租约。
        /// </summary>
        public abstract UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default) where T : Object;

        /// <summary>
        /// 异步加载资源并返回资源租约。
        /// </summary>
        public abstract UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : Object;

        /// <summary>
        /// 尝试从资源租约中读取 Unity 资源对象。
        /// </summary>
        public abstract bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset);

        #endregion

        #region 内部 Lease 方法 [INTERNAL LEASE METHODS]

        /// <summary>
        /// 获取绑定资源租约。
        /// </summary>
        internal abstract ResourceLeaseHandle AcquireBinding(ResourceKey key);

        /// <summary>
        /// 异步获取绑定资源租约。
        /// </summary>
        internal abstract UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken);

        /// <summary>
        /// 异步获取子资源绑定租约。
        /// </summary>
        internal abstract UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, EResourceLeaseOption options, CancellationToken cancellationToken);

        /// <summary>
        /// 尝试从租约获取子精灵。
        /// </summary>
        internal abstract bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite);

        /// <summary>
        /// 尝试从租约获取资源 ID。
        /// </summary>
        internal abstract bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId);

        /// <summary>
        /// 设置租约选项。
        /// </summary>
        internal abstract void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options);

        /// <summary>
        /// 获取预制体源租约。
        /// </summary>
        internal abstract ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName);

        /// <summary>
        /// 异步获取预制体源租约。
        /// </summary>
        internal abstract UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken);

        #endregion

        #region 过期回收 [EXPIRY & RECYCLING]

        /// <summary>
        /// 每帧过期处理。
        /// </summary>
        internal abstract void ProcessKeepAlive(float unscaledTime, int maxCount);

        /// <summary>
        /// 释放全部未使用资源记录。
        /// </summary>
        internal abstract int ReleaseAllUnusedAssetRecords();

        /// <summary>
        /// 强制释放全部资源记录。
        /// </summary>
        internal abstract void ForceReleaseAllAssetRecords();

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <summary>
        /// 批量获取资源信息快照。
        /// </summary>
        public abstract int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);

        #endregion
    }
}
