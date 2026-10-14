using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源记录内核的租约侧：取用与归还——租约槽、资产的三类引用计数，以及租约到资产的读取。
    /// </summary>
    /// <remarks><c>Release</c> 与 <c>AcquireLease</c> 同分部：一次归还必须原子完成"槽位回收 + 计数增减 + 入轮/出轮"。</remarks>
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
#if UNITY_EDITOR
            if (ResourceLeaseProvenance.Enabled)
            {
                var source = ResourceLeaseProvenance.Capture();
                lease.SourceDisplay = source.Display;
                lease.SourceFile = source.File;
                lease.SourceLine = source.Line;
            }
#endif
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
#if UNITY_EDITOR
            // 归还即断掉来源字符串：槽位会被复用，留着就把上一个租户的调用点算到下一个头上
            lease.SourceDisplay = null;
            lease.SourceFile = null;
            lease.SourceLine = 0;
#endif

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

#if UNITY_EDITOR
        /// <summary>
        /// 读一条租约的取用来源：句柄失效、开关未开或该次取用没采到来源时回假。
        /// </summary>
        /// <remarks>来源只在 <see cref="ResourceLeaseProvenance.Enabled"/> 为真时写入，所以为假不代表租约不是本框取的，只代表当时没记。</remarks>
        internal bool TryGetLeaseSource(ResourceLeaseHandle handle, out string display, out string file, out int line)
        {
            display = file = string.Empty;
            line = 0;
            if (!TryGetLeaseSlotIndex(handle, out int leaseIndex))
            {
                return false;
            }

            ref LeaseSlot lease = ref GetLeaseSlotRef(leaseIndex);
            if (string.IsNullOrEmpty(lease.SourceDisplay))
            {
                return false;
            }

            display = lease.SourceDisplay;
            file = lease.SourceFile;
            line = lease.SourceLine;
            return true;
        }
#endif
    }
}
