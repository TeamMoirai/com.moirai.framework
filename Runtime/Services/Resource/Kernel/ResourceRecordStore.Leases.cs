using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源记录内核的租约侧——取用与归还：租约槽、资产的三类引用计数，以及租约到资产的读取。
    /// <para><c>Release</c> 刻意与 <c>AcquireLease</c> 同分部：一次归还必须同时完成
    /// "槽位回收 + 计数增减 + 入轮/出轮"，中间不容外部代码插手。</para>
    /// </summary>
    internal sealed partial class ResourceRecordStore
    {
        internal ResourceLeaseHandle AcquireLease(int assetId, EResourceLeaseKind leaseKind,
            EResourceLeaseOption options)
        {
            if (!IsValidAssetId(assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            ref AssetSlot asset = ref GetAssetSlotRef(assetId);
            if (leaseKind == EResourceLeaseKind.Binding)
            {
                asset.BindingRefCount++;
            }
            else
            {
                asset.DirectRefCount++;
                leaseKind = EResourceLeaseKind.Direct;
            }

            asset.IdleReleaseRequested = 0;
            RemoveUnusedAssetCandidate(assetId, ref asset);
            RemoveFromExpiryQueue(assetId, ref asset);
            if (asset.KeepAliveRefCount > 0)
            {
                asset.KeepAliveRefCount = 0;
            }

            UpdateAssetState(ref asset);

            int leaseIndex = AllocateLeaseSlot();
            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.AssetId = assetId;
            lease.Kind = leaseKind;
            lease.State = EResourceLeaseState.Active;
            lease.Flags = (byte)options;
            return new ResourceLeaseHandle(leaseIndex, lease.Generation);
        }

        /// <inheritdoc />
        internal void Release(ResourceLeaseHandle handle)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            int assetId = lease.AssetId;
            EResourceLeaseOption options = (EResourceLeaseOption)lease.Flags;
            lease.State = EResourceLeaseState.Released;

            if (lease.Kind == EResourceLeaseKind.Binding)
            {
                ref AssetSlot asset = ref GetAssetSlotRef(assetId);
                if (asset.BindingRefCount > 0)
                {
                    asset.BindingRefCount--;
                }
            }
            else
            {
                ref AssetSlot asset = ref GetAssetSlotRef(assetId);
                if (asset.DirectRefCount > 0)
                {
                    asset.DirectRefCount--;
                }
            }

            if ((options & EResourceLeaseOption.KeepAliveOnRelease) != 0)
            {
                ref AssetSlot kaAsset = ref GetAssetSlotRef(assetId);
                kaAsset.KeepAliveRefCount++;
                kaAsset.KeepAliveExpireTick = ToKeepAliveTick(Time.unscaledTime) +
                    Mathf.Max(0, Mathf.CeilToInt(Host.IdleAssetExpireTime));
                AddToKeepAliveBucket(assetId, ref kaAsset);
            }

            UpdateAssetStateAndIdleQueue(assetId, ref GetAssetSlotRef(assetId));
            FreeLeaseSlot(leaseIndex);
        }

        /// <inheritdoc />
        internal bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset)
        {
            asset = null;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            if (!IsValidAssetId(lease.AssetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(lease.AssetId);
            if (slot.State == EResourceAssetState.Released || slot.Asset == null)
            {
                return false;
            }

            asset = slot.Asset;
            return true;
        }

        internal bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            if (!IsValidAssetId(lease.AssetId))
            {
                return false;
            }

            ref AssetSlot slot = ref GetAssetSlotRef(lease.AssetId);
            if (slot.State == EResourceAssetState.Released || !Host.IsHandleValid(slot.RawHandle))
            {
                return false;
            }

            sprite = Host.GetSubSprite(slot.RawHandle, spriteName);
            return sprite != null;
        }

        internal bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            assetId = -1;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            assetId = lease.AssetId;
            return IsValidAssetId(assetId);
        }

        internal void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
        {
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            lease.Flags = (byte)options;
        }
    }
}
