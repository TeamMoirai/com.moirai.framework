using System;

namespace Moirai.Atropos.Resource
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public sealed partial class ResourceHandler
    {
        #region 字段 [FIELDS]

        private int _assetRecordCapacity = 64;
        private int _assetLeaseCapacity = 128;
        private int _bindingOwnerCapacity = 64;
        private int _bindingSlotCapacity = 128;
        private int _registeredTargetCapacity = 128;
        private float _idleAssetExpireTime = 60f;

        #endregion

        #region 容量属性 [CAPACITY PROPERTIES]

        /// <inheritdoc />
        public int AssetRecordCapacity
        {
            get => _assetRecordCapacity;
            set
            {
                _assetRecordCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity, _assetRecordCapacity);
            }
        }

        /// <inheritdoc />
        public int AssetLeaseCapacity
        {
            get => _assetLeaseCapacity;
            set
            {
                _assetLeaseCapacity = value > 0 ? value : 0;
                WarmupResourceRecords(_assetRecordCapacity, _assetLeaseCapacity, _assetRecordCapacity);
            }
        }

        /// <inheritdoc />
        public int BindingOwnerCapacity
        {
            get => _bindingOwnerCapacity;
            set
            {
                _bindingOwnerCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <inheritdoc />
        public int BindingSlotCapacity
        {
            get => _bindingSlotCapacity;
            set
            {
                _bindingSlotCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <inheritdoc />
        public int RegisteredTargetCapacity
        {
            get => _registeredTargetCapacity;
            set
            {
                _registeredTargetCapacity = value > 0 ? value : 0;
                WarmupBindingRecords();
            }
        }

        /// <inheritdoc />
        public float IdleAssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value < 0f ? 0f : value;
        }

        #endregion

        #region 预热 [WARMUP]

        /// <inheritdoc />
        public void WarmupResourceRecords(int assetCapacity, int leaseCapacity, int unityObjectIndexCapacity)
        {
            _assetRecordsByKey.EnsureCapacity(assetCapacity);
            _assetRecordByLoadKeyId.EnsureCapacity(assetCapacity);
            _assetRecordHeadByUnityObjectId.EnsureCapacity(unityObjectIndexCapacity);
            _assetLoadingOperationByKey.EnsureCapacity(assetCapacity);

            if (assetCapacity > 0)
            {
                EnsureAssetSlotPage(assetCapacity - 1);
            }

            if (leaseCapacity > 0)
            {
                EnsureLeaseSlotPage(leaseCapacity - 1);
            }
        }

        private void WarmupBindingRecords()
        {
            _bindingService?.Warmup(_bindingOwnerCapacity, _bindingSlotCapacity, _registeredTargetCapacity);
            ResourceOwner.WarmupReleaseBuffer(_bindingOwnerCapacity);
        }

        #endregion

        #region 遗留池属性桥接 [LEGACY POOL BRIDGING]

        /// <summary>
        /// 资源自动释放检查间隔（秒）——桥接到 IdleAssetExpireTime。
        /// </summary>
        public float AssetAutoReleaseInterval
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value;
        }

        /// <summary>
        /// 资源容量上限——桥接到 AssetRecordCapacity。
        /// </summary>
        public int AssetCapacity
        {
            get => _assetRecordCapacity;
            set => AssetRecordCapacity = value;
        }

        /// <summary>
        /// 资源过期秒数——桥接到 IdleAssetExpireTime。
        /// </summary>
        public float AssetExpireTime
        {
            get => _idleAssetExpireTime;
            set => _idleAssetExpireTime = value;
        }

        /// <summary>
        /// 资源池优先级——保留兼容性。
        /// </summary>
        public int AssetPriority { get; set; } = 0;

        #endregion

        #region 遗留卸载桥接 [LEGACY UNLOAD BRIDGING]

        /// <inheritdoc />
        [Obsolete("Use ResourceAssetLease<T> or Binding instead of LoadAsset/UnloadAsset.")]
        public void UnloadAsset(object asset)
        {
            TryReleaseLegacyDirectByAsset(asset);
        }

        #endregion
    }
}
