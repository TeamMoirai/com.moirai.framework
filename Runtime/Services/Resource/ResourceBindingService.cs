using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源绑定服务实现，管理 Owner/Binding/Target 三级关系，
    /// 使用分页 slot 数组 + generation 验证 + 栈式 free list。
    /// </summary>
    internal sealed partial class ResourceBindingService : IResourceBindingService
    {
        #region 常量 [CONSTANTS]

        private const int PAGE_BITS = 8;

        private const int PAGE_SIZE = 1 << PAGE_BITS;

        private const int PAGE_MASK = PAGE_SIZE - 1;

        // 每帧销毁态轮转扫描的槽位配额
        private const int DESTROYED_SWEEP_BUDGET = 64;

        #endregion
        #region 结构体 [STRUCTS]

        internal struct OwnerSlot
        {
            public int OwnerId;
            public ulong GameObjectId;
            public uint Generation;
            public int BindingHead;
            public int BindingCount;
            public int RegisteredTargetHead;
            public int RegisteredTargetCount;
            public ResourceOwner Owner;
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
            public int ViewKeyId;
            public ResourceLeaseHandle Lease;
            public EResourceBindingSlotType SlotType;
            public byte Flags;
            public uint Version;
            public int NextByOwner;
            public int NextFree;
        }

        internal struct RegisteredTargetSlot
        {
            public ulong TargetComponentId;
            public int OwnerId;
            public uint OwnerGeneration;
            public int NextByOwner;
            public int NextFree;
        }

        internal readonly struct BindingSlotKey
        {
            public readonly ulong TargetComponentId;
            public readonly EResourceBindingSlotType SlotType;
            public readonly ushort SubIndex;

            public BindingSlotKey(ulong targetComponentId, EResourceBindingSlotType slotType, ushort subIndex)
            {
                TargetComponentId = targetComponentId;
                SlotType = slotType;
                SubIndex = subIndex;
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
                       SlotKey.SlotType == other.SlotKey.SlotType && SlotKey.SubIndex == other.SlotKey.SubIndex;
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
                    hash = (hash * 397) ^ SlotKey.SubIndex;
                    return hash;
                }
            }
        }

        internal readonly struct TargetOwnerEntry
        {
            public readonly int OwnerId;
            public readonly uint OwnerGeneration;

            public TargetOwnerEntry(int ownerId, uint ownerGeneration)
            {
                OwnerId = ownerId;
                OwnerGeneration = ownerGeneration;
            }
        }

        #endregion
        #region 字段 [FIELDS]

        private readonly ResourceServiceHandler _handler;

        private OwnerSlot[][] _ownerPages;

        private int _ownerNextIndex;

        private int _ownerFreeHead = -1;

        private BindingSlot[][] _bindingPages;

        private int _bindingNextIndex;

        private int _bindingFreeHead = -1;

        private RegisteredTargetSlot[][] _registeredTargetPages;

        private int _registeredTargetNextIndex;

        private int _registeredTargetFreeHead = -1;

        private readonly ResourceIndexMap<OwnerSlotKey, int> _bindingIndexByOwnerSlot = new();

        private readonly ResourceIndexMap<ulong, int> _ownerIndexByGameObjectId = new();

        private readonly ResourceIndexMap<ulong, TargetOwnerEntry> _ownerByTargetComponentId = new();

        private bool _isShutdown;

        private int _ownerSweepCursor;

        private int _bindingSweepCursor;

        #endregion
        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 创建资源绑定服务。
        /// </summary>
        /// <param name="handler">资源处理器。</param>
        public ResourceBindingService(ResourceServiceHandler handler)
        {
            _handler = handler;
        }

        #endregion
        #region IResourceBindingService 实现 [IMPLEMENTATION]

        /// <inheritdoc />
        public EResourceBindStatus RegisterOwner(ResourceOwner owner)
        {
            if (_isShutdown)
            {
                return EResourceBindStatus.ServiceShutdown;
            }

            if (owner == null || owner.gameObject == null)
            {
                return EResourceBindStatus.MissingOwner;
            }

            if (owner.IsRegistered)
            {
                return EResourceBindStatus.Success;
            }

            ulong gameObjectId = UnityObjectId.Get(owner.gameObject);
            if (_ownerIndexByGameObjectId.TryGetValue(gameObjectId, out int existingIndex))
            {
                ref OwnerSlot existing = ref GetOwnerSlotRef(existingIndex);
                if (existing.State == 1)
                {
                    owner.SetRegistered(existing.OwnerId, existing.GameObjectId, existing.Generation);
                    return EResourceBindStatus.Success;
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
            return EResourceBindStatus.Success;
        }

        /// <inheritdoc />
        public EResourceBindStatus ReleaseOwner(ResourceOwner owner)
        {
            if (owner == null || !owner.IsRegistered)
            {
                return EResourceBindStatus.MissingOwner;
            }

            return ReleaseOwner(owner.OwnerId, owner.Generation);
        }

        /// <inheritdoc />
        public EResourceBindStatus ReleaseOwner(int ownerId, uint generation)
        {
            if (_isShutdown)
            {
                return EResourceBindStatus.ServiceShutdown;
            }

            return Internal_ReleaseOwner(ownerId, generation);
        }

        /// <summary>
        /// Owner 释放核心路径，无 shutdown 守卫——仅供 <see cref="Shutdown"/> 在置位后调用。
        /// </summary>
        private EResourceBindStatus Internal_ReleaseOwner(int ownerId, uint generation)
        {
            int ownerIndex = ownerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return EResourceBindStatus.MissingOwner;
            }

            ref OwnerSlot slot = ref GetOwnerSlotRef(ownerIndex);
            if (slot.State != 1 || slot.Generation != generation)
            {
                return EResourceBindStatus.StaleOwner;
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

            int targetCurrent = slot.RegisteredTargetHead;
            while (targetCurrent >= 0)
            {
                ref RegisteredTargetSlot target = ref GetRegisteredTargetSlotRef(targetCurrent);
                int next = target.NextByOwner;
                if (_ownerByTargetComponentId.TryGetValue(target.TargetComponentId, out TargetOwnerEntry entry) &&
                    entry.OwnerId == slot.OwnerId &&
                    entry.OwnerGeneration == slot.Generation)
                {
                    _ownerByTargetComponentId.Remove(target.TargetComponentId);
                }

                FreeRegisteredTargetSlot(targetCurrent);
                targetCurrent = next;
            }

            slot.RegisteredTargetHead = -1;
            slot.RegisteredTargetCount = 0;
            slot.State = 0;
            _ownerIndexByGameObjectId.Remove(slot.GameObjectId);
            ResourceOwner ownerObject = slot.Owner;
            FreeOwnerSlot(ownerIndex);
            if (ownerObject != null && ownerObject.IsRegistered && ownerObject.Generation == generation)
            {
                ownerObject.ClearRegistered();
            }

            RethrowCollected(exceptions);

            return EResourceBindStatus.Success;
        }

        /// <inheritdoc />
        public void Warmup(int ownerCapacity, int bindingCapacity, int registeredTargetCapacity)
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

            if (registeredTargetCapacity > 0)
            {
                EnsureRegisteredTargetPage(registeredTargetCapacity - 1);
                _ownerByTargetComponentId.EnsureCapacity(registeredTargetCapacity);
            }

            ResourceOwner.WarmupReleaseBuffer(ownerCapacity);
        }

        /// <inheritdoc />
        public EResourceBindStatus RegisterTarget(ResourceOwner owner, Component target)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            ulong targetComponentId = UnityObjectId.Get(target);
            if (_ownerByTargetComponentId.TryGetValue(targetComponentId, out TargetOwnerEntry existingEntry))
            {
                if (existingEntry.OwnerId == ownerSlot.OwnerId &&
                    existingEntry.OwnerGeneration == ownerSlot.Generation)
                {
                    return EResourceBindStatus.Success;
                }

                RemoveRegisteredTargetSlot(existingEntry.OwnerId, existingEntry.OwnerGeneration,
                    targetComponentId);
            }

            _ownerByTargetComponentId.Set(targetComponentId,
                new TargetOwnerEntry(ownerSlot.OwnerId, ownerSlot.Generation));

            int targetIndex = AllocateRegisteredTargetSlot();
            ref RegisteredTargetSlot targetSlot = ref GetRegisteredTargetSlotRef(targetIndex);
            targetSlot.TargetComponentId = targetComponentId;
            targetSlot.OwnerId = ownerSlot.OwnerId;
            targetSlot.OwnerGeneration = ownerSlot.Generation;
            targetSlot.NextByOwner = ownerSlot.RegisteredTargetHead;
            ownerSlot.RegisteredTargetHead = targetIndex;
            ownerSlot.RegisteredTargetCount++;
            return EResourceBindStatus.Success;
        }

        /// <inheritdoc />
        public EResourceBindStatus UnregisterTarget(ResourceOwner owner, Component target)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            ulong targetComponentId = UnityObjectId.Get(target);
            if (_ownerByTargetComponentId.TryGetValue(targetComponentId, out TargetOwnerEntry entry) &&
                entry.OwnerId == ownerSlot.OwnerId &&
                entry.OwnerGeneration == ownerSlot.Generation)
            {
                _ownerByTargetComponentId.Remove(targetComponentId);
                RemoveRegisteredTargetSlot(ownerSlot.OwnerId, ownerSlot.Generation, targetComponentId);
            }

            return EResourceBindStatus.Success;
        }

        /// <inheritdoc />
        public int GetOwnerInfos(ResourceOwnerInfo[] results, int startIndex, int maxCount)
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
                ref ResourceOwnerInfo info = ref results[written];
                info.Active = slot.State == 1;
                info.OwnerIndex = index;
                info.OwnerId = slot.OwnerId;
                info.GameObjectId = slot.GameObjectId;
                info.Generation = slot.Generation;
                info.BindingCount = slot.BindingCount;
                info.RegisteredTargetCount = slot.RegisteredTargetCount;
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
        public int GetBindingInfos(ResourceBindingInfo[] results, int startIndex, int maxCount)
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
                ref ResourceBindingInfo info = ref results[written];
                info.Active = slot.OwnerId > 0 && slot.SlotType != EResourceBindingSlotType.None;
                info.BindingIndex = index;
                info.OwnerId = slot.OwnerId;
                info.OwnerGeneration = slot.OwnerGeneration;
                info.TargetGameObjectId = slot.TargetGameObjectId;
                info.TargetComponentId = slot.TargetComponentId;
                info.SlotKey = slot.SlotKey.TargetComponentId;
                info.AssetId = slot.AssetId;
                info.ViewKeyId = slot.ViewKeyId;
                info.Lease = slot.Lease;
                info.Version = slot.Version;
                info.SubIndex = slot.SlotKey.SubIndex;
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

        private EResourceBindStatus EnsureOwner(ResourceOwner owner, out int ownerIndex)
        {
            ownerIndex = -1;
            if (_isShutdown)
            {
                return EResourceBindStatus.ServiceShutdown;
            }

            if (owner == null)
            {
                return EResourceBindStatus.MissingOwner;
            }

            if (!owner.IsRegistered)
            {
                EResourceBindStatus status = RegisterOwner(owner);
                if (status != EResourceBindStatus.Success)
                {
                    return status;
                }
            }

            ownerIndex = owner.OwnerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return EResourceBindStatus.MissingOwner;
            }

            ref OwnerSlot slot = ref GetOwnerSlotRef(ownerIndex);
            if (slot.State != 1 || slot.Generation != owner.Generation)
            {
                return EResourceBindStatus.StaleOwner;
            }

            return EResourceBindStatus.Success;
        }

        #endregion
    }
}
