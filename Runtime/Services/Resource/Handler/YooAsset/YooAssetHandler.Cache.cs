namespace Moirai.Atropos.Resource
{
    // ReSharper disable once ClassNeverInstantiated.Global
    partial class YooAssetHandler
    {
        #region 字段 [FIELDS]

        private int _assetRecordCapacity = 64;
        private int _assetLeaseCapacity = 128;
        private int _bindingOwnerCapacity = 64;
        private int _bindingSlotCapacity = 128;
        private float _idleAssetExpireTime = 60f;
        private int _idleAssetCapacity = 256;

        #endregion

        #region 容量属性 [CAPACITY PROPERTIES]

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
