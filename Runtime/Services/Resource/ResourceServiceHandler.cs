using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 租约取用与归还的窄接缝——绑定层所需的全部后端能力，恰好八个成员。
    /// <para>之所以单独存在：绑定层此前握的是 <see cref="ResourceServiceHandler"/>，那是 74 个抽象成员
    /// 的后端全契约，而它实际调用的只有这里这 8 个（其中 27 处是 <see cref="Release"/>）。
    /// 收口之前处理器构造并驱动绑定服务、绑定服务又回调处理器的内部成员，两边都既不能单独构造也不能
    /// mock——测试只能拿一个真后端裸实例，靠它"未初始化"来凑确定性。收窄之后一条 8 成员的接缝
    /// 就能假造，且后端实现者面对的能力面第一次是可枚举的。</para>
    /// <para>八个成员在 <see cref="ResourceServiceHandler"/> 上是 <c>public abstract</c>，直接满足本接口；
    /// 程序集外的后端只要派生处理器就能落地，不再被 <c>internal abstract</c> 锁在框架内。</para>
    /// </summary>
    internal interface IResourceLeaseSource
    {
        /// <summary>同步取用一个直接租约；失败返回 <see cref="ResourceLeaseHandle.Invalid"/>。</summary>
        ResourceLeaseHandle AcquireBinding(ResourceKey key);

        /// <summary>只读缓存取用：已加载则租约，未命中返回 false 且不发起加载。</summary>
        bool TryAcquireBindingCached(ResourceKey key, out ResourceLeaseHandle handle);

        /// <summary>异步取用一个直接租约。</summary>
        UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken);

        /// <summary>异步取用整张子资源图集的租约，具体精灵再按名索取。</summary>
        UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName,
            EResourceLeaseOption options, CancellationToken cancellationToken);

        /// <summary>从子资源图集租约里按名取出一个精灵。</summary>
        bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite);

        /// <summary>取出租约指向的资源对象。</summary>
        bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset);

        /// <summary>取出租约指向的记录 id，仅用于诊断。</summary>
        bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId);

        /// <summary>登记取用时的租约选项（如释放后转保活）。</summary>
        void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options);

        /// <summary>归还租约。<b>必须无条件完成</b>——静默丢掉一条就是永久泄漏。</summary>
        void Release(ResourceLeaseHandle handle);
    }

    /// <summary>
    /// 资源管理器处理器抽象基类（策略模式抽象策略）——定义通用资源加载、缓存、租约与绑定契约。
    /// <para>框架通用，不依赖具体资源系统（YooAsset、Addressable 等）；
    /// 由具体后端（如 <see cref="YooAssetHandler"/>、<see cref="AddressableHandler"/>）实现。</para>
    /// <para>由 <see cref="ResourceServiceSettings"/> 序列化配置，<see cref="ResourceService"/> 外观转发调用。</para>
    /// </summary>
    [Serializable]
    public abstract class ResourceServiceHandler : FrameworkHandler, IResourceLeaseSource
    {
        #region 基础属性 [BASE PROPERTIES]

        /// <summary>
        /// 默认资源包/资源组名称。
        /// </summary>
        public abstract string DefaultPackageName { get; set; }

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
        /// 初始化指定资源包，返回初始化结果；<paramref name="needInitManifest"/> 为 true 时顺带请求并更新清单。
        /// <para>与 <see cref="TryInitializePackageAsync"/> 的关系：本方法是原语（返回操作句柄），
        /// 布尔薄壳在它之上叠了远程地址写入并把结果收成 <c>bool</c>。</para>
        /// </summary>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="needInitManifest">是否需要初始化清单。</param>
        /// <returns>资源包初始化结果。</returns>
        public abstract UniTask<ResourcePackageInitResult> InitializePackageAsync(string packageName, bool needInitManifest = false);

        /// <summary>
        /// 初始化指定资源包并收成成败布尔——<see cref="InitializePackageAsync"/> 的便捷薄壳：
        /// 非空的 <paramref name="hostServerURL"/> / <paramref name="fallbackHostServerURL"/> 写入
        /// <see cref="HostServerURL"/> / <see cref="FallbackHostServerURL"/> 后再初始化，**不更新清单**。
        /// <para>并发去重与幂等语义与 <see cref="InitializePackageAsync"/> 一致。</para>
        /// </summary>
        /// <param name="packageName">资源包名称。为空时使用默认资源包。</param>
        /// <param name="hostServerURL">资源服务器地址。非空时写入 <see cref="HostServerURL"/>。</param>
        /// <param name="fallbackHostServerURL">备用资源服务器地址。非空时写入 <see cref="FallbackHostServerURL"/>。</param>
        /// <returns>初始化是否成功。</returns>
        public abstract UniTask<bool> TryInitializePackageAsync(string packageName = "", string hostServerURL = "", string fallbackHostServerURL = "");

        #endregion

        #region 包管理 [PACKAGE MANAGEMENT]

        /// <summary>
        /// 获取指定资源包的版本。
        /// </summary>
        public abstract string GetPackageVersion(string customPackageName = "");

        /// <summary>
        /// 异步请求最新包版本。
        /// </summary>
        public abstract ResourcePackageVersionResult RequestPackageVersion(bool appendTimeTicks = false, int timeout = 60, string customPackageName = "");

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
        public abstract ResourceClearCacheResult StartClearCache(EResourceClearMode clearMode, string customPackageName = "");

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

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 通过资源系统异步加载场景——场景资源经后端（YooAsset、Addressable 等）管线加载，而非引擎内建管线。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载（加载至待激活状态后保持挂起，由 <see cref="ResourceSceneHandle.UnSuspend"/> 解除）。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="packageName">资源包名称。为空时使用默认资源包。</param>
        /// <returns>场景加载句柄。</returns>
        public abstract ResourceSceneHandle LoadSceneAsync(string location, LoadSceneMode sceneMode, bool suspendLoad, uint priority, string packageName = "");

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
        /// 无引用资源句柄空闲过期秒数。
        /// </summary>
        public abstract float IdleAssetExpireTime { get; set; }

        /// <summary>
        /// 空闲资源记录容量上限：无引用记录数超过该值时，等待过期最久（即最长空闲）的记录立即释放，
        /// 不必等到 <see cref="IdleAssetExpireTime"/> 到期。取 0 表示不留任何空闲记录。
        /// </summary>
        public abstract int IdleAssetCapacity { get; set; }

        #endregion

        #region 预热 [WARMUP]

        /// <summary>
        /// 预热资源记录。
        /// </summary>
        public abstract void WarmupResourceRecords(int assetCapacity, int leaseCapacity);

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
        /// 释放一个显式资源租约。
        /// </summary>
        public abstract void Release(ResourceLeaseHandle handle);

        /// <summary>
        /// 同步加载资源并返回资源租约。
        /// </summary>
        public abstract ResourceAssetLease<T> LoadLease<T>(ResourceKey key) where T : UObject;

        /// <summary>
        /// 同步加载资源并返回资源租约。
        /// </summary>
        public abstract ResourceAssetLease<T> LoadLease<T>(string location, string packageName = "") where T : UObject;

        /// <summary>
        /// 异步加载资源并返回资源租约。
        /// </summary>
        public abstract UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(ResourceKey key, CancellationToken cancellationToken = default) where T : UObject;

        /// <summary>
        /// 异步加载资源并返回资源租约。
        /// </summary>
        public abstract UniTask<ResourceAssetLease<T>> LoadLeaseAsync<T>(string location, CancellationToken cancellationToken = default, string packageName = "") where T : UObject;

        /// <summary>
        /// 尝试从资源租约中读取 Unity 资源对象。
        /// </summary>
        public abstract bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset);

        #endregion

        #region 内部 Lease 方法 [INTERNAL LEASE METHODS]

        /// <summary>
        /// 获取绑定资源租约。
        /// </summary>
        public abstract ResourceLeaseHandle AcquireBinding(ResourceKey key);

        /// <summary>
        /// 只读缓存取用绑定租约——未命中不发起后端加载（列表滑动等热路径用）。
        /// </summary>
        public abstract bool TryAcquireBindingCached(ResourceKey key, out ResourceLeaseHandle handle);

        /// <summary>
        /// 异步获取绑定资源租约。
        /// </summary>
        public abstract UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken);

        /// <summary>
        /// 异步获取子资源绑定租约。
        /// </summary>
        public abstract UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName, EResourceLeaseOption options, CancellationToken cancellationToken);

        /// <summary>
        /// 尝试从租约获取子精灵。
        /// </summary>
        public abstract bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite);

        /// <summary>
        /// 尝试从租约获取资源 ID。
        /// </summary>
        public abstract bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId);

        /// <summary>
        /// 设置租约选项。
        /// </summary>
        public abstract void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options);

        /// <summary>
        /// 获取预制体源租约。
        /// </summary>
        public abstract ResourceLeaseHandle AcquirePrefabSourceLease(string location, string packageName);

        /// <summary>
        /// 异步获取预制体源租约。
        /// </summary>
        public abstract UniTask<ResourceLeaseHandle> AcquirePrefabSourceLeaseAsync(string location, string packageName, CancellationToken cancellationToken);

        #endregion

        #region 过期回收 [EXPIRY & RECYCLING]

        /// <summary>
        /// 每帧资源维护：空闲/保活到期回收 + 销毁态所有者与绑定的兜底回收。
        /// </summary>
        /// <param name="unscaledTime">本帧的无缩放时间。</param>
        /// <param name="expireBudget">本轮可处理的到期记录数上限。</param>
        /// <param name="destroySweepBudget">销毁态轮转每帧查验的槽位数（所有者与绑定各一份）。
        /// 它与 <paramref name="expireBudget"/> 是两件事，分开传：合成一个预算会让到期记录多的帧饿死销毁回收。</param>
        public abstract void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget);

        /// <summary>
        /// 释放全部未使用资源记录。
        /// </summary>
        public abstract int ReleaseAllUnusedAssetRecords();

        /// <summary>
        /// 强制释放全部资源记录。
        /// </summary>
        public abstract void ForceReleaseAllAssetRecords();

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <summary>
        /// 批量获取资源信息快照。
        /// </summary>
        public abstract int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount);

        #endregion

        /// <summary>
        /// 取或挂实例上的 <see cref="ResourceOwner"/> 并向绑定服务登记。属表现层动作、与后端无关，
        /// 故放基类：两个后端的实例化路径此前各抄了一份，改一份忘一份就是下一处漂移。
        /// </summary>
        protected ResourceOwner EnsureResourceOwner(GameObject root)
        {
            ResourceOwner owner = root.GetComponent<ResourceOwner>();
            if (owner == null)
            {
                owner = root.AddComponent<ResourceOwner>();
            }

            BindingService?.RegisterOwner(owner);
            return owner;
        }

    }
}
