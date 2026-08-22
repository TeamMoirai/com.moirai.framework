using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源管理器接口。
    /// </summary>
    public interface IResourceService
    {
        #region 基础属性 [BASE PROPERTIES]

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        string ApplicableGameVersion { get; }

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        int InternalResourceVersion { get; }

        /// <summary>
        /// 获取或设置运行模式。
        /// </summary>
        EPlayMode PlayMode { get; set; }

        /// <summary>
        /// 资源加密方式。
        /// </summary>
        EEncryptorType EncryptorType { get; set; }

        /// <summary>
        /// 是否边玩边下载。
        /// </summary>
        bool UpdatableWhilePlaying { get; set; }

        /// <summary>
        /// 同时下载的最大数目。
        /// </summary>
        int DownloadingMaxNum { get; set; }

        /// <summary>
        /// 失败重试最大数目。
        /// </summary>
        int FailedTryAgain { get; set; }

        /// <summary>
        /// 资源绑定服务。
        /// </summary>
        IResourceBindingService BindingService { get; }

        /// <summary>
        /// 默认资源包。
        /// </summary>
        ResourcePackage DefaultPackage { get; }

        #endregion

        #region 容量与过期 [CAPACITY & EXPIRY]

        /// <summary>
        /// 获取或设置资源记录预热容量。
        /// </summary>
        int AssetRecordCapacity { get; set; }

        /// <summary>
        /// 获取或设置资源租约预热容量。
        /// </summary>
        int AssetLeaseCapacity { get; set; }

        /// <summary>
        /// 获取或设置绑定所有者预热容量。
        /// </summary>
        int BindingOwnerCapacity { get; set; }

        /// <summary>
        /// 获取或设置绑定槽位预热容量。
        /// </summary>
        int BindingSlotCapacity { get; set; }

        /// <summary>
        /// 获取或设置已注册目标预热容量。
        /// </summary>
        int RegisteredTargetCapacity { get; set; }

        /// <summary>
        /// 获取或设置无引用资源句柄进入 Idle 后的过期秒数。
        /// </summary>
        float IdleAssetExpireTime { get; set; }

        /// <summary>
        /// 预热资源记录。
        /// </summary>
        /// <param name="assetCapacity">资源记录容量。</param>
        /// <param name="leaseCapacity">租约容量。</param>
        /// <param name="unityObjectIndexCapacity">Unity 对象索引容量。</param>
        void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity);

        /// <summary>
        /// 批量获取资源信息。
        /// </summary>
        /// <param name="results">结果数组。</param>
        /// <param name="startIndex">起始索引。</param>
        /// <param name="maxCount">最大数量。</param>
        /// <returns>实际写入数量。</returns>
        int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化接口。
        /// </summary>
        void Initialize();

        /// <summary>
        /// 初始化操作。
        /// </summary>
        /// <param name="customPackageName">资源包名称。</param>
        /// <param name="needInitManifest">是否需要直接初始化资源清单。</param>
        /// <remarks>单机 OtherPackage 使用，热更项目建议修改 Procedure</remarks>
        UniTask<InitializePackageOperation> InitPackage(string customPackageName, bool needInitManifest = false);

        /// <summary>
        /// 默认资源包名称。
        /// </summary>
        string DefaultPackageName { get; set; }

        /// <summary>
        /// 获取或设置异步系统参数，每帧执行消耗的最大时间切片（单位：毫秒）。
        /// </summary>
        long Milliseconds { get; set; }

        /// <summary>
        /// 自动释放资源引用计数为0的资源包
        /// </summary>
        bool AutoUnloadBundleWhenUnused { get; set; }

        /// <summary>
        /// 热更URL，资源服务器地址。
        /// </summary>
        string HostServerURL { get; set; }
        
        /// <summary>
        /// 备用热更URL。
        /// </summary>
        string FallbackHostServerURL { get; set; }

        /// <summary>
        /// WebGL平台加载本地资源/加载远程资源。
        /// </summary>
        ELoadResWayWebGL LoadResWayWebGL { get; set; }

        /// <summary>
        /// 获取或设置资源对象池自动释放可释放对象的间隔秒数。
        /// </summary>
        float AssetAutoReleaseInterval { get; set; }

        /// <summary>
        /// 获取或设置资源对象池的容量。
        /// </summary>
        int AssetCapacity { get; set; }

        /// <summary>
        /// 获取或设置资源对象池对象过期秒数。
        /// </summary>
        float AssetExpireTime { get; set; }

        /// <summary>
        /// 获取或设置资源对象池的优先级。
        /// </summary>
        int AssetPriority { get; set; }

        #endregion

        #region 租约 API [LEASE API]

        /// <summary>
        /// 使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        /// <param name="key">资源 Key。</param>
        /// <returns>资源租约句柄，失败时返回无效句柄。</returns>
        ResourceLeaseHandle AcquireDirect(ResourceKey key);

        /// <summary>
        /// 异步获取一个直接资源租约。
        /// </summary>
        /// <param name="key">资源 Key。</param>
        /// <param name="cancellationToken">取消操作 Token。</param>
        /// <returns>资源租约句柄，失败时返回无效句柄。</returns>
        UniTask<ResourceLeaseHandle> AcquireDirectAsync(ResourceKey key,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 尝试使用显式资源 Key 获取一个直接资源租约。
        /// </summary>
        /// <param name="key">资源 Key。</param>
        /// <param name="handle">获取到的资源租约句柄。</param>
        /// <returns>是否获取成功。</returns>
        bool TryAcquireDirect(ResourceKey key, out ResourceLeaseHandle handle);

        /// <summary>
        /// 释放一个显式资源租约。
        /// </summary>
        /// <param name="handle">资源租约句柄。</param>
        void Release(ResourceLeaseHandle handle);

        /// <summary>
        /// 同步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        /// <param name="key">资源 Key。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源租约，失败时返回无效租约。</returns>
        ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : UnityEngine.Object;

        /// <summary>
        /// 同步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源租约，失败时返回无效租约。</returns>
        ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        /// <param name="key">资源 Key。</param>
        /// <param name="cancellationToken">取消操作 Token。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源租约，失败时返回无效租约。</returns>
        UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源并返回资源租约。调用方必须在不再使用资源时调用 Dispose 释放租约。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消操作 Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源租约，失败时返回无效租约。</returns>
        UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location,
            CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object;

        /// <summary>
        /// 尝试从资源租约中读取 Unity 资源对象。
        /// </summary>
        /// <param name="handle">资源租约句柄。</param>
        /// <param name="asset">Unity 资源对象。</param>
        /// <returns>是否读取成功。</returns>
        bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UnityEngine.Object asset);

        #endregion

        #region 遗留 API [LEGACY API]

        /// <summary>
        /// 卸载资源。
        /// </summary>
        /// <param name="asset">要卸载的资源。</param>
        [Obsolete("Use ResourceAssetLease<T> or Binding instead of LoadAsset/UnloadAsset.")]
        void UnloadAsset(object asset);

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）
        /// </summary>
        void UnloadUnusedAssets();

        /// <summary>
        /// 资源回收。
        /// </summary>
        /// <param name="force">为 true 时忽略 Idle 过期时间，立即释放无引用记录。</param>
        void UnloadUnusedAssets(bool force);

        /// <summary>
        /// 强制回收所有资源
        /// </summary>
        void ForceUnloadAllAssets();

        /// <summary>
        /// 强制执行释放未被使用的资源。
        /// </summary>
        /// <param name="performGCCollect">是否使用垃圾回收。</param>
        void ForceUnloadUnusedAssets(bool performGCCollect);

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>检查资源是否存在的结果。</returns>
        public HasAssetResult HasAsset(string location, string packageName = "");

        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        /// <param name="location">资源的定位地址</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        bool IsLocationValid(string location, string packageName = "");

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="resTag">资源标签。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源信息列表。</returns>
        AssetInfo[] GetAssetInfos(string resTag, string packageName = "");

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tags">资源标签列表。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源信息列表。</returns>
        AssetInfo[] GetAssetInfos(string[] tags, string packageName = "");

        /// <summary>
        /// 获取资源信息。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源信息。</returns>
        AssetInfo GetAssetInfo(string location, string packageName = "");

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        void LoadAssetAsync(string location, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "");

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="assetType">要加载的资源类型。</param>
        /// <param name="priority">加载资源的优先级。</param>
        /// <param name="loadAssetCallbacks">加载资源回调函数集。</param>
        /// <param name="userData">用户自定义数据。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包。</param>
        void LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "");

        /// <summary>
        /// 同步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        /// <returns>资源实例。</returns>
        [Obsolete("Use LoadLease<T> for explicit ownership.")]
        T LoadAsset<T>(string location, string packageName = "") where T : UnityEngine.Object;

        /// <summary>
        /// 同步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="assetType">要加载的资源类型。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源实例。</returns>
        UnityEngine.Object LoadAsset(string location, Type assetType, string packageName = "");

        /// <summary>
        /// 同步加载游戏物体并实例化。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="parent">资源实例父节点。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源实例。</returns>
        /// <remarks>会实例化资源到场景，无需主动UnloadAsset，Destroy时自动UnloadAsset。</remarks>
        GameObject LoadGameObject(string location, Transform parent = null, string packageName = "");

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="callback">回调函数。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        UniTaskVoid LoadAsset<T>(string location, Action<T> callback, string packageName = "") where T : UnityEngine.Object;
        
        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="cancellationToken">取消操作Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">要加载资源的类型。</typeparam>
        /// <returns>异步资源实例。</returns>
        [Obsolete("Use LoadLeaseAsync<T> for explicit ownership.")]
        UniTask<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="assetType">要加载的资源类型。</param>
        /// <param name="cancellationToken">取消操作Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>异步资源实例。</returns>
        UniTask<UnityEngine.Object> LoadAssetAsync(string location, Type assetType, CancellationToken cancellationToken = default, string packageName = "");

        /// <summary>
        /// 异步加载游戏物体并实例化。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="parent">资源实例父节点。</param>
        /// <param name="cancellationToken">取消操作Token。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>异步游戏物体实例。</returns>
        /// <remarks>会实例化资源到场景，无需主动UnloadAsset，Destroy时自动UnloadAsset。</remarks>
        UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default, string packageName = "");

        /// <summary>
        /// 获取同步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        AssetHandle LoadAssetSyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object;

        /// <summary>
        /// 获取同步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="assetType">资源类型。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源操作句柄。</returns>
        AssetHandle LoadAssetSyncHandle(string location, Type assetType, string packageName = "");

        /// <summary>
        /// 获取异步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源操作句柄。</returns>
        AssetHandle LoadAssetAsyncHandle<T>(string location, string packageName = "") where T : UnityEngine.Object;

        /// <summary>
        /// 获取异步加载的资源操作句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="type">资源类型。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源操作句柄。</returns>
        AssetHandle LoadAssetAsyncHandle(string location, Type type, string packageName = "");

        /// <summary>
        /// 清理包裹未使用的缓存文件。
        /// </summary>
        /// <param name="options">清理缓存的操作选项。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        ClearCacheOperation ClearCacheAsync(ClearCacheOptions options, string customPackageName = "");

        /// <summary>
        /// 清理沙盒路径。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        void ClearAllBundleFiles(string customPackageName = "");

        /// <summary>
        /// 创建资源下载器，用于下载当前资源版本所有的资源包文件。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        ResourceDownloaderOperation CreateResourceDownloader(string customPackageName = "");

        /// <summary>
        /// 当前最新的包裹版本。
        /// </summary>
        string PackageVersion { set; get; }

        /// <summary>
        /// 获取当前资源包版本。
        /// </summary>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>资源包版本。</returns>
        string GetPackageVersion(string customPackageName = "");

        /// <summary>
        /// 异步更新最新包的版本。
        /// </summary>
        /// <param name="appendTimeTicks">请求URL是否需要带时间戳。</param>
        /// <param name="timeout">超时时间。</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        /// <returns>请求远端包裹的最新版本操作句柄。</returns>
        RequestPackageVersionOperation RequestPackageVersionAsync(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "");

        /// <summary>
        /// 向网络端请求并更新清单
        /// </summary>
        /// <param name="packageVersion">更新的包裹版本</param>
        /// <param name="timeout">超时时间（默认值：60秒）</param>
        /// <param name="customPackageName">指定资源包的名称。不传使用默认资源包</param>
        LoadPackageManifestOperation LoadPackageManifestAsync(string packageVersion, int timeout = 60, string customPackageName = "");

        /// <summary>
        /// 设置远程服务Url。
        /// </summary>
        /// <param name="defaultHostServer">默认远端资源地址。</param>
        /// <param name="fallbackHostServer">备用远端资源地址。</param>
        void SetRemoteServicesUrl(string defaultHostServer, string fallbackHostServer);

        /// <summary>
        /// 低内存行为。
        /// </summary>
        void OnLowMemory();
        
        /// <summary>
        /// 低内存回调保护。
        /// </summary>
        /// <param name="action">低内存行为。</param>
        void SetForceUnloadUnusedAssetsAction(Action<bool> action);

        #endregion
    }
}