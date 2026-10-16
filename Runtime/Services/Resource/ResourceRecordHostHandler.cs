using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;
using static Moirai.Atropos.LogHandler;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 记录内核宿主处理器：资源后端（框架内建的 YooAsset、以及插件分支装进来的 Addressables 等）共用面的落点——内核懒建、容量与预热、
    /// 绑定服务、租约与维护转发、回收模板；三个原生句柄算子由后端覆写。
    /// </summary>
    /// <remarks>
    /// 本类实现 <see cref="IResourceRecordHost"/>：句柄算子转调抽象成员，配置三项读数由容量属性隐式满足。 <br />
    /// 外部自定义后端仍直接继承 <see cref="ResourceServiceHandler"/>，不经过本类。 <br />
    /// 本类公开是为了让插件装配里的后端能派生；记录内核本身保持 <c>internal</c>，装配外的派生方一律经
    /// 「派生后端接缝」那组 <c>protected internal</c> 成员取用内核能力，签名里不出现内核类型。
    /// </remarks>
    [Serializable]
    public abstract class ResourceRecordHostHandler : ResourceServiceHandler, IResourceRecordHost
    {
        #region 记录内核 [RECORD KERNEL]

        [NonSerialized] private ResourceRecordStore _store;

        /// <summary>记录内核；懒建，默认包名活读当前配置。</summary>
        /// <remarks>保持内部：内核是框架的机制类，装配外的派生后端只经下面的接缝成员用它。</remarks>
        internal ResourceRecordStore Store => _store ??= new ResourceRecordStore(this, () => DefaultPackageName);

        /// <summary>在途去重槽数量——测试观测点。</summary>
        internal int LoadingOperationCount => Store.LoadingOperationCount;

        /// <summary>校验后端原生句柄是否存活。</summary>
        protected abstract bool IsHandleValid(object handle);

        /// <summary>释放后端原生句柄；已失效句柄安全跳过。</summary>
        protected abstract void DisposeHandle(object handle);

        /// <summary>从图集形态句柄按名取子精灵；非图集形态返回 null。</summary>
        protected abstract Sprite GetSubSprite(object handle, string spriteName);

        // 接口接线转调抽象算子（接口成员隐式实现要求 public，故显式接线）。
        bool IResourceRecordHost.IsHandleValid(object handle) => IsHandleValid(handle);

        void IResourceRecordHost.DisposeHandle(object handle) => DisposeHandle(handle);

        Sprite IResourceRecordHost.GetSubSprite(object handle, string spriteName) => GetSubSprite(handle, spriteName);

        #endregion

        #region 派生后端接缝 [DERIVED BACKEND SEAMS]

        // 这组 protected internal 成员是记录内核开给「住在装配外的派生后端」的唯一取用面：
        // 内核类型（ResourceRecordStore / ResourceKeyCodec / ResourceBindingService）与它的三条私有轴
        // （句柄轴 EResourceHandleKind、租约轴 EResourceLeaseKind、失败分级 ELogLevel）一律不外露——
        // 轴位与分级折进方法名（AssetRecordKey / SubAssetsRecordKey、Direct / Binding 两条租约、
        // 两条 FailLoading 重载），后端原生句柄只以 object 进、以 object 出。

        /// <summary>包名归一：空包名取当前默认包名（活读配置，不捕获旧值）。</summary>
        protected internal string NormalizePackageName(string packageName) => Store.NormalizePackageName(packageName);

        /// <summary>资源种类归一：<see cref="EResourceAssetKind.Unknown"/> 时按类型推断。</summary>
        protected internal EResourceAssetKind NormalizeAssetKind(Type assetType, EResourceAssetKind assetKind) =>
            ResourceKeyCodec.NormalizeAssetKind(assetType, assetKind);

        /// <summary>资源类型归一：按种类收成内核登记用的实际类型。</summary>
        protected internal Type NormalizeAssetType(Type assetType, EResourceAssetKind assetKind) =>
            ResourceKeyCodec.NormalizeAssetType(assetType, assetKind);

        /// <summary>由资源类型推断种类。</summary>
        protected internal EResourceAssetKind InferAssetKind(Type assetType) => ResourceKeyCodec.InferAssetKind(assetType);

        /// <summary>
        /// 单资产记录的 packed key（句柄轴固定为单资产）。三条名称轴当场登记，与 <see cref="GetSubAssetsRecordKey"/> 不同轴不可混用。
        /// </summary>
        protected internal ulong GetAssetRecordKey(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind) =>
            Store.GetAssetRecordKey(packageName, location, assetType, assetKind, EResourceHandleKind.AssetHandle);

        /// <summary>图集子资源记录的 packed key（类型 / 种类 / 句柄三轴都固定为图集形态）。</summary>
        protected internal ulong GetSubAssetsRecordKey(string packageName, string location) =>
            Store.GetAssetRecordKey(packageName, location, typeof(Sprite), EResourceAssetKind.SubAssets,
                EResourceHandleKind.SubAssetsHandle);

        /// <summary>在途加载去重键（与记录键是两条轴位，不可互换）。</summary>
        protected internal ulong GetLoadingOperationKey(string location, string packageName, Type assetType,
            EResourceAssetKind assetKind) => Store.GetLoadingOperationKey(location, packageName, assetType, assetKind);

        /// <summary>按 packed key 查记录槽号；不新增登记，查不到就是没有。</summary>
        protected internal bool TryGetRecordId(ulong key, out int assetId) => Store.TryGetRecordId(key, out assetId);

        /// <summary>只读缓存取单资产记录（句柄轴固定为单资产）；命中时同时给出槽号与资产。</summary>
        protected internal bool TryGetCachedAssetRecord(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, out int assetId, out UObject asset) =>
            Store.TryGetCachedAssetRecord(packageName, location, assetType, assetKind, EResourceHandleKind.AssetHandle,
                out assetId, out asset);

        /// <summary>按已打包的图集 key 直查缓存记录（热路径专用，跳过三条名称轴的字典往返）。</summary>
        protected internal bool TryGetCachedSubAssetsRecordByKey(ulong key, out int assetId) =>
            Store.TryGetCachedSubAssetsRecordByKey(key, out assetId);

        /// <summary>
        /// 建/并单资产记录并把后端原生句柄挂上去（句柄轴固定为单资产）。
        /// </summary>
        /// <remarks><paramref name="assetHandle"/> 由内核转交 <see cref="IResourceRecordHost"/> 的三个句柄算子解释，内核不认识后端类型。</remarks>
        protected internal int GetOrCreateAssetRecord(string packageName, string location, Type assetType,
            EResourceAssetKind assetKind, UObject asset, object assetHandle) =>
            Store.GetOrCreateAssetRecord(packageName, location, assetType, assetKind, EResourceHandleKind.AssetHandle,
                asset, assetHandle);

        /// <summary>按已打包的图集 key 建/并子资源记录。</summary>
        protected internal int GetOrCreateSubAssetsRecordByKey(ulong key, object subAssetsHandle) =>
            Store.GetOrCreateSubAssetsRecordByKey(key, subAssetsHandle);

        /// <summary>
        /// 抢占在途去重槽：返回真值即本次调用是赢家，必须自己收尾（<see cref="CompleteLoading"/> 或 <see cref="FailLoading(ulong, Exception)"/>），
        /// 否则同 key 的等待者再也醒不过来。
        /// </summary>
        protected internal bool TryBeginLoading(ulong loadingKey) => Store.TryBeginLoading(loadingKey);

        /// <summary>等赢家的加载结果；等待者计数与去重槽回收都在内核里闭环。</summary>
        protected internal UniTask<bool> WaitForLoadingAsync(ulong loadingKey, CancellationToken cancellationToken) =>
            Store.WaitForLoadingAsync(loadingKey, cancellationToken);

        /// <summary>赢家成功收尾：完成去重槽并唤醒等待者。</summary>
        protected internal void CompleteLoading(ulong loadingKey) => Store.CompleteLoading(loadingKey);

        /// <summary>赢家失败收尾（意外异常档：以 Error 留痕）。</summary>
        protected internal void FailLoading(ulong loadingKey, Exception exception) =>
            Store.FailLoading(loadingKey, exception);

        /// <summary>赢家失败收尾（可预期的加载失败档：降为 Warning，对齐 RETHROW 分级约定）。</summary>
        protected internal void FailLoadingAsWarning(ulong loadingKey, Exception exception) =>
            Store.FailLoading(loadingKey, exception, ELogLevel.Warning);

        /// <summary>在途状态是否仍然作数（没被强卸载或关停换掉世代）。</summary>
        protected internal bool IsLoadingStateCurrent(int loadGeneration) => Store.IsLoadingStateCurrent(loadGeneration);

        /// <summary>
        /// 调用方取消后是否放弃这次赢家收尾：只有确认没有别的等待者才真放弃。
        /// </summary>
        protected internal bool ShouldAbortLoadingAfterCallerCancellation(ulong loadingKey,
            CancellationToken cancellationToken, ref bool callerCancellationRequested) =>
            Store.ShouldAbortLoadingAfterCallerCancellation(loadingKey, cancellationToken,
                ref callerCancellationRequested);

        /// <summary>取一条直接引用租约（内核的直接引用计数轴）。</summary>
        protected internal ResourceLeaseHandle AcquireDirectLease(int assetId, EResourceLeaseOption options) =>
            Store.AcquireLease(assetId, EResourceLeaseKind.Direct, options);

        /// <summary>取一条绑定引用租约（内核的绑定引用计数轴）。</summary>
        protected internal ResourceLeaseHandle AcquireBindingLease(int assetId, EResourceLeaseOption options) =>
            Store.AcquireLease(assetId, EResourceLeaseKind.Binding, options);

        /// <summary>内核是否已进入销毁态（回收与关停路径的守卫读数）。</summary>
        protected internal bool IsRecordStoreDestroying => Store._isDestroying;

        /// <summary>卸载世代读数：实例化前后各取一次比对，跨越了强卸载就丢弃这次结果。</summary>
        protected internal uint UnloadGeneration => Store._unloadGeneration;

        /// <summary>建绑定服务实例，宿主为本处理器（租约能力经框架内部的窄接缝交出去，后端不必认识绑定服务类型）。</summary>
        protected internal void CreateBindingService() => _bindingService = new ResourceBindingService(this);

        /// <summary>关停绑定服务：此后注册与绑定一律以 <see cref="EResourceBindStatus.ServiceShutdown"/> 拒绝。</summary>
        protected internal void ShutdownBindingService() => _bindingService?.Shutdown();

        /// <summary>把预制体源登记给该所有者；登记失败时调用方须自己销毁实例并归还租约。</summary>
        protected internal EResourceBindStatus RegisterPrefabSource(ResourceOwner owner, ResourceLeaseHandle lease,
            UObject prefabSource) => _bindingService.RegisterPrefabSource(owner, lease, prefabSource);

        #endregion

        #region 容量属性 [CAPACITY PROPERTIES]

        // 夹取后落进字段，生效值即字段值：记录内核按 IResourceRecordHost 活读这三项，
        // 留成裸自动属性会让"写进去的值"与"内核读到的值"分家。
        [NonSerialized] private int _assetRecordCapacity = 64;
        [NonSerialized] private int _assetLeaseCapacity = 128;
        [NonSerialized] private int _bindingOwnerCapacity = 64;
        [NonSerialized] private int _bindingSlotCapacity = 128;
        [NonSerialized] private float _idleAssetExpireTime = 60f;
        [NonSerialized] private int _idleAssetCapacity = 256;

        /// <inheritdoc />
        public sealed override int AssetRecordCapacity
        {
            get => _assetRecordCapacity;
            set
            {
                _assetRecordCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity);
            }
        }

        /// <inheritdoc />
        public sealed override int AssetLeaseCapacity
        {
            get => _assetLeaseCapacity;
            set
            {
                _assetLeaseCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity);
            }
        }

        /// <inheritdoc />
        public sealed override int BindingOwnerCapacity
        {
            get => _bindingOwnerCapacity;
            set
            {
                _bindingOwnerCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <inheritdoc />
        public sealed override int BindingSlotCapacity
        {
            get => _bindingSlotCapacity;
            set
            {
                _bindingSlotCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <inheritdoc />
        public sealed override float IdleAssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value < 0f ? 0f : value;
        }

        /// <inheritdoc />
        public sealed override int IdleAssetCapacity
        {
            get => _idleAssetCapacity;
            set
            {
                _idleAssetCapacity = value < 0 ? 0 : value;
                // 不当场淘汰：那等于把一次 O(n) 突发挂在一次属性赋值上。
                Store.RequestIdleCapacityTrim();
            }
        }

        #endregion

        #region 预热 [WARMUP]

        /// <inheritdoc />
        public sealed override void WarmupResourceRecords(int assetCapacity, int leaseCapacity)
        {
            Store.EnsureRecordCapacity(assetCapacity);
            Store.EnsureLoadingOperationCapacity(assetCapacity);

            if (assetCapacity > 0)
            {
                Store.EnsureAssetSlotPage(assetCapacity - 1);
            }

            if (leaseCapacity > 0)
            {
                Store.EnsureLeaseSlotPage(leaseCapacity - 1);
            }
        }

        /// <summary>按绑定容量预热绑定服务与 ResourceOwner 释放缓冲。</summary>
        protected void WarmupBindingRecords()
        {
            _bindingService?.Warmup(_bindingOwnerCapacity, _bindingSlotCapacity);
            ResourceOwner.WarmupReleaseBuffer(_bindingOwnerCapacity);
        }

        #endregion

        #region 绑定服务 [BINDING SERVICE]

        /// <summary>绑定服务实例。</summary>
        /// <remarks>保持内部：绑定服务是框架机制类，装配外的派生后端经 <see cref="CreateBindingService"/> 等接缝成员用它。</remarks>
        internal ResourceBindingService _bindingService;

        /// <inheritdoc />
        public sealed override IResourceBindingService BindingService => _bindingService;

        #endregion

        #region 租约转发 [LEASE FORWARDS]

        /// <inheritdoc />
        public sealed override void Release(ResourceLeaseHandle handle) => Store.Release(handle);

        /// <inheritdoc />
        public sealed override bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset) =>
            Store.TryGetLeaseAsset(handle, out asset);

        /// <inheritdoc />
        public sealed override bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName,
            out Sprite sprite) => Store.TryGetSubSpriteAsset(handle, spriteName, out sprite);

        /// <inheritdoc />
        public sealed override bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId) =>
            Store.TryGetLeaseAssetId(handle, out assetId);

        /// <inheritdoc />
        public sealed override void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options) =>
            Store.SetLeaseOptions(handle, options);

        #endregion

        #region 每帧维护与诊断 [MAINTENANCE & DIAGNOSTICS]

        /// <inheritdoc />
        public sealed override void ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)
        {
            // 销毁态兜底回收先于预算判定，也先于内核的到期走查：没有到期记录可处理时，
            // 被销毁对象的槽位照样要收——这条顺序是这段代码存在的理由，别调换。
            _bindingService?.ProcessDestroyedObjects(destroySweepBudget);
            Store.ProcessResourceMaintenance(unscaledTime, expireBudget);
        }

        /// <inheritdoc />
        public sealed override int ReleaseAllUnusedAssetRecords() => Store.ReleaseAllUnusedAssetRecords();

        /// <inheritdoc />
        public sealed override void ForceReleaseAllAssetRecords() => Store.ForceReleaseAllAssetRecords();

        /// <inheritdoc />
        public sealed override int GetAssetInfos(ResourceAssetInfo[] results, int startIndex, int maxCount) =>
            Store.GetAssetInfos(results, startIndex, maxCount);

#if UNITY_EDITOR
        /// <inheritdoc />
        internal sealed override int GetLeaseSourceInfos(ResourceLeaseSourceInfo[] results, int startIndex, int maxCount,
            out int outstandingCount, out int sourcedCount) =>
            Store.GetLeaseSourceInfos(results, startIndex, maxCount, out outstandingCount, out sourcedCount);
#endif

        #endregion

        #region 资源回收 [ASSET RECYCLING]

        /// <summary>强制卸载回调；由 <see cref="ResourceService"/> 初始化时登记。</summary>
        protected Action<bool> _forceUnloadUnusedAssetsAction;

        /// <summary>
        /// 低内存行为。
        /// </summary>
        /// <remarks>这份委托由 <see cref="ResourceService"/> 初始化时登记（RequestForceUnloadUnusedAssets）；吞掉它，<c>Application.lowMemory</c> 就没人接。</remarks>
        public sealed override void OnLowMemory()
        {
            _forceUnloadUnusedAssetsAction?.Invoke(true);
        }

        /// <summary>
        /// 设置强制卸载未使用资源回调。
        /// </summary>
        public sealed override void SetForceUnloadUnusedAssetsAction(Action<bool> action)
        {
            _forceUnloadUnusedAssetsAction = action;
        }

        /// <summary>
        /// 资源回收（卸载引用计数为零的资源）。
        /// </summary>
        public sealed override void UnloadUnusedAssets()
        {
            UnloadUnusedAssets(false);
        }

        /// <summary>
        /// 资源回收。
        /// </summary>
        public override void UnloadUnusedAssets(bool force)
        {
            // 强制档先还掉引用计数为零的记录；bundle 级卸载是后端各有其物的部分，走钩子。
            if (force)
            {
                ReleaseAllUnusedAssetRecords();
            }

            UnloadUnusedBundles();
        }

        /// <summary>bundle 级未用卸载钩子；有对应操作的后端（YooAsset）覆写，无对应物者保持空实现。</summary>
        protected virtual void UnloadUnusedBundles()
        {
        }

        #endregion
    }
}
