using System;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 记录内核宿主处理器：两个资源后端（YooAsset / Addressables）共用面的落点——内核懒建、容量与预热、
    /// 绑定服务、租约与维护转发、回收模板；三个原生句柄算子由后端覆写。
    /// </summary>
    /// <remarks>
    /// 本类实现 <see cref="IResourceRecordHost"/>：句柄算子转调抽象成员，配置三项读数由容量属性隐式满足。 <br />
    /// 外部自定义后端仍直接继承 <see cref="ResourceServiceHandler"/>，不经过本类。
    /// </remarks>
    [Serializable]
    internal abstract class ResourceRecordHostHandler : ResourceServiceHandler, IResourceRecordHost
    {
        #region 记录内核 [RECORD KERNEL]

        [NonSerialized] private ResourceRecordStore _store;

        /// <summary>记录内核；懒建，默认包名活读当前配置。</summary>
        protected ResourceRecordStore Store => _store ??= new ResourceRecordStore(this, () => DefaultPackageName);

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
        protected ResourceBindingService _bindingService;

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
