#if ADDRESSABLES_INSTALLED
using System;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的容量与预热面：夹取后落字段，生效值即字段值。
    /// </summary>
    /// <remarks>记录内核按 IResourceRecordHost 活读这几项，留成裸自动属性会让"写进去的值"与"内核读到的值"分家。</remarks>
    partial class AddressableHandler
    {
        #region 容量属性 [CAPACITY PROPERTIES]

        /// <inheritdoc />
        // 夹取后落进字段，生效值即字段值：记录内核按 IResourceRecordHost 活读这三项，
        // 留成裸自动属性会让"写进去的值"与"内核读到的值"分家（这正是审计点名的静默 no-op）。
        [NonSerialized] private int _assetRecordCapacity = 64;
        [NonSerialized] private int _assetLeaseCapacity = 128;

        /// <inheritdoc />
        public override int AssetRecordCapacity
        {
            get => _assetRecordCapacity;
            set
            {
                _assetRecordCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity);
            }
        }

        /// <inheritdoc />
        public override int AssetLeaseCapacity
        {
            get => _assetLeaseCapacity;
            set
            {
                _assetLeaseCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity);
            }
        }

        [NonSerialized] private int _bindingOwnerCapacity = 64;
        [NonSerialized] private int _bindingSlotCapacity = 128;

        /// <inheritdoc />
        public override int BindingOwnerCapacity
        {
            get => _bindingOwnerCapacity;
            set
            {
                _bindingOwnerCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <inheritdoc />
        public override int BindingSlotCapacity
        {
            get => _bindingSlotCapacity;
            set
            {
                _bindingSlotCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <inheritdoc />
        [NonSerialized] private float _idleAssetExpireTime = 60f;
        [NonSerialized] private int _idleAssetCapacity = 256;

        /// <inheritdoc />
        public override float IdleAssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value < 0f ? 0f : value;
        }

        /// <inheritdoc />
        public override int IdleAssetCapacity
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
        public override void WarmupResourceRecords(int assetCapacity, int leaseCapacity)
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

        private void WarmupBindingRecords()
        {
            _bindingService?.Warmup(_bindingOwnerCapacity, _bindingSlotCapacity);
            ResourceOwner.WarmupReleaseBuffer(_bindingOwnerCapacity);
        }

        #endregion
    }
}
#endif
