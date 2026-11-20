using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// 资源绑定服务实现：管理 Owner/Binding/Target 三级关系，底部分页 slot 数组 + generation 验证 + 栈式 free list。
    /// </summary>
    internal sealed partial class AssetBindingService : IAssetBindingService
    {
        #region 常量 [CONSTANTS]

        private const int PAGE_BITS = 8;

        private const int PAGE_SIZE = 1 << PAGE_BITS;

        private const int PAGE_MASK = PAGE_SIZE - 1;

        #endregion
        #region 结构体 [STRUCTS]

        internal struct OwnerSlot
        {
            public int OwnerId;
            public ulong GameObjectId;
            public uint Generation;
            public int BindingHead;
            public int BindingCount;
            public AssetOwner Owner;
            public byte State;
            public int NextFree;
        }

        internal struct BindingSlot
        {
            public BindingSlotKey SlotKey;
            public int OwnerId;
            public uint OwnerGeneration;
            public ulong TargetGameObjectId;
            public ulong TargetComponentId;
            public Component Target;
            public UObject AppliedAsset;
            public UObject RuntimeObject;
            public int AssetId;
            public AssetLeaseHandle Lease;
            public EAssetBindingSlotType SlotType;
            public byte Flags;
            public uint Version;
            public int NextByOwner;
            public int NextFree;
        }

        internal readonly struct BindingSlotKey
        {
            public readonly ulong TargetComponentId;
            public readonly EAssetBindingSlotType SlotType;

            public BindingSlotKey(ulong targetComponentId, EAssetBindingSlotType slotType)
            {
                TargetComponentId = targetComponentId;
                SlotType = slotType;
            }
        }

        internal readonly struct OwnerSlotKey : System.IEquatable<OwnerSlotKey>
        {
            public readonly int OwnerId;
            public readonly BindingSlotKey SlotKey;

            public OwnerSlotKey(int ownerId, BindingSlotKey slotKey)
            {
                OwnerId = ownerId;
                SlotKey = slotKey;
            }

            public bool Equals(OwnerSlotKey other)
            {
                return OwnerId == other.OwnerId && SlotKey.TargetComponentId == other.SlotKey.TargetComponentId &&
                       SlotKey.SlotType == other.SlotKey.SlotType;
            }

            public override bool Equals(object obj)
            {
                return obj is OwnerSlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = OwnerId;
                    hash = (hash * 397) ^ SlotKey.TargetComponentId.GetHashCode();
                    hash = (hash * 397) ^ (int)SlotKey.SlotType;
                    return hash;
                }
            }
        }

        #endregion
        #region 字段 [FIELDS]

        private readonly IAssetLeaseSource _leaseSource;

        private OwnerSlot[][] _ownerPages;

        private int _ownerNextIndex;

        private int _ownerFreeHead = -1;

        private BindingSlot[][] _bindingPages;

        private int _bindingNextIndex;

        private int _bindingFreeHead = -1;

        private readonly AssetIndexMap<OwnerSlotKey, int> _bindingIndexByOwnerSlot = new();

        private readonly AssetIndexMap<ulong, int> _ownerIndexByGameObjectId = new();

        private bool _isShutdown;

        private int _ownerSweepCursor;

        private int _bindingSweepCursor;

        #endregion
        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 创建资源绑定服务。
        /// </summary>
        /// <param name="leaseSource">租约提供方（<see cref="IAssetLeaseSource"/> 窄接缝， <br />
        /// 而非整个 <see cref="IAssetService"/>）。</param>
        public AssetBindingService(IAssetLeaseSource leaseSource)
        {
            _leaseSource = leaseSource;
        }

        #endregion
        #region IAssetBindingService 实现 [IMPLEMENTATION]

        /// <inheritdoc />
        public EAssetBindStatus RegisterOwner(AssetOwner owner)
        {
            if (_isShutdown)
            {
                return EAssetBindStatus.ServiceShutdown;
            }

            if (owner == null || owner.gameObject == null)
            {
                return EAssetBindStatus.MissingOwner;
            }

            if (owner.IsRegistered)
            {
                return EAssetBindStatus.Success;
            }

            ulong gameObjectId = UnityObjectId.Get(owner.gameObject);
            if (_ownerIndexByGameObjectId.TryGetValue(gameObjectId, out int existingIndex))
            {
                ref OwnerSlot existing = ref GetOwnerSlotRef(existingIndex);
                if (existing.State == 1)
                {
                    owner.SetRegistered(existing.OwnerId, existing.GameObjectId, existing.Generation);
                    return EAssetBindStatus.Success;
                }
            }

            int index = AllocateOwnerSlot();
            ref OwnerSlot slot = ref GetOwnerSlotRef(index);
            int ownerId = index + 1;

            owner.SetRegistered(ownerId, gameObjectId, slot.Generation);

            slot.OwnerId = ownerId;
            slot.GameObjectId = gameObjectId;
            slot.Owner = owner;
            slot.State = 1;
            _ownerIndexByGameObjectId.Set(gameObjectId, index);
            return EAssetBindStatus.Success;
        }

        /// <inheritdoc />
        public EAssetBindStatus ReleaseOwner(AssetOwner owner)
        {
            if (owner == null || !owner.IsRegistered)
            {
                return EAssetBindStatus.MissingOwner;
            }

            return ReleaseOwner(owner.OwnerId, owner.Generation);
        }

        /// <inheritdoc />
        public EAssetBindStatus ReleaseOwner(int ownerId, uint generation)
        {
            if (_isShutdown)
            {
                return EAssetBindStatus.ServiceShutdown;
            }

            return Internal_ReleaseOwner(ownerId, generation);
        }

        /// <summary>
        /// Owner 释放核心路径，无 shutdown 守卫——仅供 <see cref="Shutdown"/> 在置位后调用。
        /// </summary>
        private EAssetBindStatus Internal_ReleaseOwner(int ownerId, uint generation)
        {
            int ownerIndex = ownerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return EAssetBindStatus.MissingOwner;
            }

            ref OwnerSlot slot = ref GetOwnerSlotRef(ownerIndex);
            if (slot.State != 1 || slot.Generation != generation)
            {
                return EAssetBindStatus.StaleOwner;
            }

            List<Exception> exceptions = null;
            int current = slot.BindingHead;
            while (current >= 0)
            {
                ref BindingSlot binding = ref GetBindingSlotRef(current);
                int next = binding.NextByOwner;
                OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerId, binding.SlotKey);

                // 单条绑定抛出会截断该所有者剩余绑定，槽位与租约就此长留，故逐条隔离后收尾再抛。
                try
                {
                    ClearAndReleaseBinding(ref binding);
                }
                catch (Exception exception)
                {
                    CollectException(ref exceptions, exception);
                }

                _bindingIndexByOwnerSlot.Remove(ownerSlotKey);
                FreeBindingSlot(current);
                current = next;
            }

            slot.BindingHead = -1;
            slot.BindingCount = 0;

            slot.State = 0;
            _ownerIndexByGameObjectId.Remove(slot.GameObjectId);
            AssetOwner ownerObject = slot.Owner;
            FreeOwnerSlot(ownerIndex);
            if (ownerObject != null && ownerObject.IsRegistered && ownerObject.Generation == generation)
            {
                ownerObject.ClearRegistered();
            }

            RethrowCollected(exceptions);

            return EAssetBindStatus.Success;
        }

        /// <inheritdoc />
        public void Warmup(int ownerCapacity, int bindingCapacity)
        {
            if (ownerCapacity > 0)
            {
                EnsureOwnerPage(ownerCapacity - 1);
                _ownerIndexByGameObjectId.EnsureCapacity(ownerCapacity);
            }

            if (bindingCapacity > 0)
            {
                EnsureBindingPage(bindingCapacity - 1);
                _bindingIndexByOwnerSlot.EnsureCapacity(bindingCapacity);
            }

            AssetOwner.WarmupReleaseBuffer(ownerCapacity);
        }

        /// <inheritdoc />
        public int GetOwnerInfos(AssetOwnerInfo[] results, int startIndex, int maxCount)
        {
            int total = _ownerNextIndex;
            if (results == null || maxCount <= 0 || startIndex >= total)
            {
                return total;
            }

            int writeLimit = Math.Min(Math.Min(maxCount, results.Length), total - Math.Max(0, startIndex));
            int written = 0;
            int index = Math.Max(0, startIndex);
            while (index < total && written < writeLimit)
            {
                ref OwnerSlot slot = ref GetOwnerSlotRef(index);
                ref AssetOwnerInfo info = ref results[written];
                info.Active = slot.State == 1;
                info.OwnerIndex = index;
                info.OwnerId = slot.OwnerId;
                info.GameObjectId = slot.GameObjectId;
                info.Generation = slot.Generation;
                info.BindingCount = slot.BindingCount;
                info.HasOwnerObject = slot.Owner != null;
#if UNITY_EDITOR
                info.OwnerObject = slot.Owner != null ? slot.Owner.gameObject : null;
#endif
                written++;
                index++;
            }

            return total;
        }

        /// <inheritdoc />
        public int GetBindingInfos(AssetBindingInfo[] results, int startIndex, int maxCount)
        {
            int total = _bindingNextIndex;
            if (results == null || maxCount <= 0 || startIndex >= total)
            {
                return total;
            }

            int writeLimit = Math.Min(Math.Min(maxCount, results.Length), total - Math.Max(0, startIndex));
            int written = 0;
            int index = Math.Max(0, startIndex);
            while (index < total && written < writeLimit)
            {
                ref BindingSlot slot = ref GetBindingSlotRef(index);
                ref AssetBindingInfo info = ref results[written];
                info.Active = slot.OwnerId > 0 && slot.SlotType != EAssetBindingSlotType.None;
                info.BindingIndex = index;
                info.OwnerId = slot.OwnerId;
                info.OwnerGeneration = slot.OwnerGeneration;
                info.TargetGameObjectId = slot.TargetGameObjectId;
                info.TargetComponentId = slot.TargetComponentId;
                info.SlotKey = slot.SlotKey.TargetComponentId;
                info.AssetId = slot.AssetId;
                info.Lease = slot.Lease;
                info.Version = slot.Version;
                info.SlotType = slot.SlotType;
                info.HasAppliedAsset = slot.AppliedAsset != null;
                info.HasRuntimeObject = slot.RuntimeObject != null;
#if UNITY_EDITOR
                info.TargetObject = slot.Target;
#endif
                written++;
                index++;
            }

            return total;
        }

        #endregion
        #region 辅助方法 [UTILITY METHODS]

        private EAssetBindStatus EnsureOwner(AssetOwner owner, out int ownerIndex)
        {
            ownerIndex = -1;
            if (_isShutdown)
            {
                return EAssetBindStatus.ServiceShutdown;
            }

            if (owner == null)
            {
                return EAssetBindStatus.MissingOwner;
            }

            if (!owner.IsRegistered)
            {
                EAssetBindStatus status = RegisterOwner(owner);
                if (status != EAssetBindStatus.Success)
                {
                    return status;
                }
            }

            ownerIndex = owner.OwnerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return EAssetBindStatus.MissingOwner;
            }

            ref OwnerSlot slot = ref GetOwnerSlotRef(ownerIndex);
            if (slot.State != 1 || slot.Generation != owner.Generation)
            {
                return EAssetBindStatus.StaleOwner;
            }

            return EAssetBindStatus.Success;
        }

        #endregion
    }
}
