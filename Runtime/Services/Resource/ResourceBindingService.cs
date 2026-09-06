using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源绑定服务实现，管理 Owner/Binding/Target 三级关系，
    /// 使用分页 slot 数组 + generation 验证 + 栈式 free list。
    /// </summary>
    internal sealed class ResourceBindingService : IResourceBindingService
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
            public Object AppliedAsset;
            public Object RuntimeObject;
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

            int current = slot.BindingHead;
            while (current >= 0)
            {
                ref BindingSlot binding = ref GetBindingSlotRef(current);
                int next = binding.NextByOwner;
                ClearAndReleaseBinding(ref binding);
                _bindingIndexByOwnerSlot.Remove(new OwnerSlotKey(ownerId, binding.SlotKey));
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
        public EResourceBindStatus BindSprite(ResourceOwner owner, Image image, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (image == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            ResourceKey spriteKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Sprite), EResourceAssetKind.Sprite)
                : key;
            ResourceLeaseHandle newLease = _handler.AcquireBinding(spriteKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_handler.TryGetLeaseAsset(newLease, out Object asset) || asset is not Sprite sprite)
            {
                _handler.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            if (!ApplySprite(image, sprite))
            {
                _handler.Release(newLease);
                return EResourceBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, image, newLease, sprite, EResourceBindingSlotType.ImageSprite, options, 0);
        }

        /// <inheritdoc />
        public EResourceBindStatus BindSprite(ResourceOwner owner, SpriteRenderer spriteRenderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (spriteRenderer == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            ResourceKey spriteKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Sprite), EResourceAssetKind.Sprite)
                : key;
            ResourceLeaseHandle newLease = _handler.AcquireBinding(spriteKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_handler.TryGetLeaseAsset(newLease, out Object asset) || asset is not Sprite sprite)
            {
                _handler.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            if (!ApplySprite(spriteRenderer, sprite))
            {
                _handler.Release(newLease);
                return EResourceBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, spriteRenderer, newLease, sprite,
                EResourceBindingSlotType.SpriteRendererSprite, options, 0);
        }

        /// <inheritdoc />
        public async UniTask<EResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, Image image,
            ResourceKey atlasKey, string spriteName, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            if (image == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            // SetNativeSize 由 RegisterSpriteSource 统一处理。
            return await BindSubSpriteSourceAsync(owner, image, atlasKey, spriteName,
                EResourceBindingSlotType.SubSprite, options, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask<EResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, SpriteRenderer spriteRenderer,
            ResourceKey atlasKey, string spriteName, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            if (spriteRenderer == null)
            {
                return UniTask.FromResult(EResourceBindStatus.MissingTarget);
            }

            return BindSubSpriteSourceAsync(owner, spriteRenderer, atlasKey, spriteName,
                EResourceBindingSlotType.SpriteRendererSprite, options, cancellationToken);
        }

        private async UniTask<EResourceBindStatus> BindSubSpriteSourceAsync(ResourceOwner owner, Component target,
            ResourceKey atlasKey, string spriteName, EResourceBindingSlotType slotType,
            EResourceBindingOption options, CancellationToken cancellationToken)
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

            EResourceBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, target, slotType,
                out int ownerId, out uint ownerGeneration, out ulong targetComponentId,
                out ulong targetGameObjectId, out BindingSlotKey slotKey, out uint requestVersion);
            if (reserveStatus != EResourceBindStatus.Success)
            {
                return reserveStatus;
            }

            ResourceLeaseHandle newLease = await _handler.AcquireSubAssetsBindingAsync(
                atlasKey.Location, atlasKey.PackageName,
                ToLeaseOptions(options), cancellationToken);
            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, target))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!_handler.TryGetSubSpriteAsset(newLease, spriteName, out Sprite sprite))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, target))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EResourceBindStatus.LoadFailed
                    : EResourceBindStatus.StaleOwner;
            }

            if (!ApplySprite(target, sprite))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, target, newLease, sprite, slotType, options, requestVersion);
        }

        /// <inheritdoc />
        public EResourceBindStatus BindImageMaterial(ResourceOwner owner, Image image, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int _);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (image == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            ResourceKey materialKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Material), EResourceAssetKind.Material)
                : key;
            ResourceLeaseHandle newLease = _handler.AcquireBinding(materialKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_handler.TryGetLeaseAsset(newLease, out Object asset) || asset is not Material mat)
            {
                _handler.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            if (!ApplyMaterial(image, mat))
            {
                _handler.Release(newLease);
                return EResourceBindStatus.ApplyFailed;
            }

            return RegisterMaterialSource(owner, image, newLease, mat, null,
                EResourceBindingSlotType.ImageMaterial, options, 0);
        }

        /// <inheritdoc />
        public async UniTask<EResourceBindStatus> BindImageMaterialAsync(ResourceOwner owner, Image image,
            ResourceKey key, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (image == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            EResourceBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, image,
                EResourceBindingSlotType.ImageMaterial, out int ownerId, out uint ownerGeneration,
                out ulong targetComponentId, out ulong targetGameObjectId, out BindingSlotKey slotKey,
                out uint requestVersion);
            if (reserveStatus != EResourceBindStatus.Success)
            {
                return reserveStatus;
            }

            ResourceKey materialKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Material), EResourceAssetKind.Material)
                : key;
            ResourceLeaseHandle newLease = await _handler.AcquireBindingAsync(materialKey, cancellationToken);
            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, image))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!_handler.TryGetLeaseAsset(newLease, out Object asset) || asset is not Material mat)
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, image))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EResourceBindStatus.LoadFailed
                    : EResourceBindStatus.StaleOwner;
            }

            if (!ApplyMaterial(image, mat))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.ApplyFailed;
            }

            return RegisterMaterialSource(owner, image, newLease, mat, null,
                EResourceBindingSlotType.ImageMaterial, options, requestVersion);
        }

        /// <inheritdoc />
        public EResourceBindStatus BindSharedMaterial(ResourceOwner owner, Renderer renderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None)
        {
            return BindMaterialInternal(owner, renderer, key, false, options, 0, default, false);
        }

        /// <inheritdoc />
        public UniTask<EResourceBindStatus> BindSharedMaterialAsync(ResourceOwner owner, Renderer renderer,
            ResourceKey key, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            return BindMaterialAsyncInternal(owner, renderer, key, false, options, cancellationToken);
        }

        /// <inheritdoc />
        public EResourceBindStatus BindMaterialInstance(ResourceOwner owner, Renderer renderer, ResourceKey key,
            EResourceBindingOption options = EResourceBindingOption.None)
        {
            return BindMaterialInternal(owner, renderer, key, true, options, 0, default, false);
        }

        /// <inheritdoc />
        public UniTask<EResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner owner, Renderer renderer,
            ResourceKey key, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            return BindMaterialAsyncInternal(owner, renderer, key, true, options, cancellationToken);
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

        #region 内部注册方法 [INTERNAL REGISTRATION]

        internal EResourceBindStatus RegisterPrefabSource(ResourceOwner owner, ResourceLeaseHandle lease,
            Object prefabSource)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            BindingSlotKey slotKey = new BindingSlotKey(ownerSlot.GameObjectId,
                EResourceBindingSlotType.PrefabSource, 0);
            OwnerSlotKey key = new OwnerSlotKey(ownerSlot.OwnerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(key, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(key, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            ResourceLeaseHandle oldLease = binding.Lease;
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerSlot.OwnerId;
            binding.TargetGameObjectId = ownerSlot.GameObjectId;
            binding.TargetComponentId = ownerSlot.GameObjectId;
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = owner;
            binding.AppliedAsset = prefabSource;
            binding.RuntimeObject = null;
            binding.AssetId = _handler.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.ViewKeyId = 0;
            binding.Lease = lease;
            binding.SlotType = EResourceBindingSlotType.PrefabSource;
            binding.Flags = (byte)EResourceBindingOption.KeepAliveOnRelease;
            _handler.SetLeaseOptions(lease, EResourceLeaseOption.KeepAliveOnRelease);
            binding.Version++;
            if (oldLease.IsValid)
            {
                _handler.Release(oldLease);
            }

            return EResourceBindStatus.Success;
        }

        internal EResourceBindStatus RegisterSpriteSource(ResourceOwner owner, Component target,
            ResourceLeaseHandle lease, Sprite sprite, EResourceBindingSlotType slotType)
        {
            return RegisterSpriteSource(owner, target, lease, sprite, slotType, EResourceBindingOption.None, 0);
        }

        private EResourceBindStatus RegisterSpriteSource(ResourceOwner owner, Component target,
            ResourceLeaseHandle lease, Sprite sprite, EResourceBindingSlotType slotType,
            EResourceBindingOption options, uint reservedVersion)
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

            if (!lease.IsValid || sprite == null)
            {
                return EResourceBindStatus.InvalidKey;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            BindingSlotKey slotKey = new BindingSlotKey(UnityObjectId.Get(target), slotType, 0);
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerSlot.OwnerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(ownerSlotKey, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            ResourceLeaseHandle oldLease = binding.Lease;
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerSlot.OwnerId;
            binding.TargetGameObjectId = UnityObjectId.Get(target.gameObject);
            binding.TargetComponentId = UnityObjectId.Get(target);
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = target;
            binding.AppliedAsset = sprite;
            binding.RuntimeObject = null;
            binding.AssetId = _handler.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.ViewKeyId = 0;
            binding.Lease = lease;
            binding.SlotType = slotType;
            binding.Flags = (byte)options;
            _handler.SetLeaseOptions(lease, ToLeaseOptions(options));
            if (reservedVersion != 0 && binding.Version == reservedVersion)
            {
                binding.Version = reservedVersion;
            }
            else
            {
                binding.Version++;
            }

            if (oldLease.IsValid)
            {
                _handler.Release(oldLease);
            }

            if ((options & EResourceBindingOption.SetNativeSize) != 0 && target is Image img)
            {
                img.SetNativeSize();
            }

            return EResourceBindStatus.Success;
        }

        internal EResourceBindStatus RegisterMaterialSource(ResourceOwner owner, Component target,
            ResourceLeaseHandle lease, Material appliedMaterial, Material runtimeMaterial,
            EResourceBindingSlotType slotType)
        {
            return RegisterMaterialSource(owner, target, lease, appliedMaterial, runtimeMaterial, slotType,
                EResourceBindingOption.None, 0);
        }

        private EResourceBindStatus RegisterMaterialSource(ResourceOwner owner, Component target,
            ResourceLeaseHandle lease, Material appliedMaterial, Material runtimeMaterial,
            EResourceBindingSlotType slotType, EResourceBindingOption options, uint reservedVersion)
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

            if (!lease.IsValid || appliedMaterial == null)
            {
                return EResourceBindStatus.InvalidKey;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            BindingSlotKey slotKey = new BindingSlotKey(UnityObjectId.Get(target), slotType, 0);
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerSlot.OwnerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(ownerSlotKey, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            ResourceLeaseHandle oldLease = binding.Lease;
            Object oldRuntimeObject = binding.RuntimeObject;
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerSlot.OwnerId;
            binding.TargetGameObjectId = UnityObjectId.Get(target.gameObject);
            binding.TargetComponentId = UnityObjectId.Get(target);
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = target;
            binding.AppliedAsset = appliedMaterial;
            binding.RuntimeObject = runtimeMaterial;
            binding.AssetId = _handler.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.ViewKeyId = 0;
            binding.Lease = lease;
            binding.SlotType = slotType;
            binding.Flags = (byte)options;
            _handler.SetLeaseOptions(lease, ToLeaseOptions(options));
            if (reservedVersion != 0 && binding.Version == reservedVersion)
            {
                binding.Version = reservedVersion;
            }
            else
            {
                binding.Version++;
            }

            if (oldRuntimeObject != null)
            {
                Object.Destroy(oldRuntimeObject);
            }

            if (oldLease.IsValid)
            {
                _handler.Release(oldLease);
            }

            return EResourceBindStatus.Success;
        }

        #endregion

        #region 材质绑定内部 [MATERIAL BIND INTERNALS]

        private EResourceBindStatus BindMaterialInternal(ResourceOwner owner, Renderer renderer, ResourceKey key,
            bool createRuntimeInstance, EResourceBindingOption options, uint reservedVersion,
            BindingSlotKey _, bool isAsync)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int _);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (renderer == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            ResourceKey materialKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Material), EResourceAssetKind.Material)
                : key;
            ResourceLeaseHandle newLease = _handler.AcquireBinding(materialKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_handler.TryGetLeaseAsset(newLease, out Object asset) || asset is not Material sourceMat)
            {
                _handler.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            Material appliedMaterial = sourceMat;
            Material runtimeMaterial = null;
            EResourceBindingSlotType slotType = createRuntimeInstance
                ? EResourceBindingSlotType.RendererMaterialInstance
                : EResourceBindingSlotType.RendererSharedMaterial;

            if (createRuntimeInstance)
            {
                runtimeMaterial = Object.Instantiate(sourceMat);
                appliedMaterial = runtimeMaterial;
            }

            if (!ApplyMaterial(renderer, appliedMaterial))
            {
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _handler.Release(newLease);
                return EResourceBindStatus.ApplyFailed;
            }

            EResourceBindStatus registerStatus = RegisterMaterialSource(owner, renderer, newLease,
                appliedMaterial, runtimeMaterial, slotType, options, 0);
            if (registerStatus != EResourceBindStatus.Success)
            {
                ClearMaterialSlot(renderer, appliedMaterial, runtimeMaterial, slotType);
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _handler.Release(newLease);
            }

            return registerStatus;
        }

        private async UniTask<EResourceBindStatus> BindMaterialAsyncInternal(ResourceOwner owner, Renderer renderer,
            ResourceKey key, bool createRuntimeInstance, EResourceBindingOption options,
            CancellationToken cancellationToken)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            if (renderer == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            EResourceBindingSlotType slotType = createRuntimeInstance
                ? EResourceBindingSlotType.RendererMaterialInstance
                : EResourceBindingSlotType.RendererSharedMaterial;

            EResourceBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, renderer, slotType,
                out int ownerId, out uint ownerGeneration, out ulong targetComponentId,
                out ulong targetGameObjectId, out BindingSlotKey slotKey, out uint requestVersion);
            if (reserveStatus != EResourceBindStatus.Success)
            {
                return reserveStatus;
            }

            ResourceKey materialKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Material), EResourceAssetKind.Material)
                : key;
            ResourceLeaseHandle newLease = await _handler.AcquireBindingAsync(materialKey, cancellationToken);
            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!_handler.TryGetLeaseAsset(newLease, out Object asset) || asset is not Material sourceMat)
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EResourceBindStatus.LoadFailed
                    : EResourceBindStatus.StaleOwner;
            }

            Material appliedMaterial = sourceMat;
            Material runtimeMaterial = null;
            if (createRuntimeInstance)
            {
                runtimeMaterial = Object.Instantiate(sourceMat);
                appliedMaterial = runtimeMaterial;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!ApplyMaterial(renderer, appliedMaterial))
            {
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _handler.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.ApplyFailed;
            }

            EResourceBindStatus registerStatus = RegisterMaterialSource(owner, renderer, newLease,
                appliedMaterial, runtimeMaterial, slotType, options, requestVersion);
            if (registerStatus != EResourceBindStatus.Success)
            {
                ClearMaterialSlot(renderer, appliedMaterial, runtimeMaterial, slotType);
                if (runtimeMaterial != null)
                {
                    Object.Destroy(runtimeMaterial);
                }

                _handler.Release(newLease);
            }

            return registerStatus;
        }

        #endregion

        #region 异步绑定安全 [ASYNC BINDING SAFETY]

        private EResourceBindStatus ReserveBindingRequest(int ownerIndex, Component target,
            EResourceBindingSlotType slotType, out int ownerId, out uint ownerGeneration,
            out ulong targetComponentId, out ulong targetGameObjectId, out BindingSlotKey slotKey,
            out uint requestVersion)
        {
            ownerId = 0;
            ownerGeneration = 0;
            targetComponentId = 0;
            targetGameObjectId = 0;
            slotKey = default;
            requestVersion = 0;

            if (target == null || target.gameObject == null)
            {
                return EResourceBindStatus.MissingTarget;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            ownerId = ownerSlot.OwnerId;
            ownerGeneration = ownerSlot.Generation;
            targetComponentId = UnityObjectId.Get(target);
            targetGameObjectId = UnityObjectId.Get(target.gameObject);
            slotKey = new BindingSlotKey(targetComponentId, slotType, 0);
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                bindingIndex = AllocateBindingSlot();
                ref BindingSlot newBinding = ref GetBindingSlotRef(bindingIndex);
                newBinding.NextByOwner = ownerSlot.BindingHead;
                ownerSlot.BindingHead = bindingIndex;
                ownerSlot.BindingCount++;
                _bindingIndexByOwnerSlot.Set(ownerSlotKey, bindingIndex);
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerId;
            binding.TargetGameObjectId = targetGameObjectId;
            binding.TargetComponentId = targetComponentId;
            binding.OwnerGeneration = ownerGeneration;
            binding.Target = target;
            binding.SlotType = slotType;
            binding.Version++;
            requestVersion = binding.Version;
            return EResourceBindStatus.Success;
        }

        private void CancelReservedBindingRequest(int ownerId, uint ownerGeneration, BindingSlotKey slotKey,
            uint requestVersion)
        {
            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerId, slotKey);
            if (!_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                return;
            }

            ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
            if (binding.OwnerGeneration != ownerGeneration ||
                binding.Version != requestVersion ||
                binding.Lease.IsValid ||
                binding.AppliedAsset != null ||
                binding.RuntimeObject != null)
            {
                return;
            }

            int ownerIndex = ownerId - 1;
            if (IsValidOwnerIndex(ownerIndex))
            {
                ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
                if (owner.State == 1 && owner.Generation == ownerGeneration)
                {
                    UnlinkBindingFromOwner(ref owner, bindingIndex);
                }
            }

            _bindingIndexByOwnerSlot.Remove(ownerSlotKey);
            FreeBindingSlot(bindingIndex);
        }

        private bool IsBindingRequestCurrent(int ownerId, uint ownerGeneration, ulong targetComponentId,
            ulong targetGameObjectId, BindingSlotKey slotKey, uint requestVersion, Component target)
        {
            if (_isShutdown || target == null || UnityObjectId.Get(target) != targetComponentId)
            {
                return false;
            }

            if (target.gameObject == null || UnityObjectId.Get(target.gameObject) != targetGameObjectId)
            {
                return false;
            }

            int ownerIndex = ownerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return false;
            }

            ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
            if (owner.State != 1 || owner.Generation != ownerGeneration)
            {
                return false;
            }

            OwnerSlotKey ownerSlotKey = new OwnerSlotKey(ownerId, slotKey);
            if (_bindingIndexByOwnerSlot.TryGetValue(ownerSlotKey, out int bindingIndex))
            {
                ref BindingSlot binding = ref GetBindingSlotRef(bindingIndex);
                return binding.OwnerGeneration == ownerGeneration &&
                       binding.TargetComponentId == targetComponentId &&
                       binding.Version == requestVersion;
            }

            return false;
        }

        #endregion

        #region 辅助方法 [UTILITY METHODS]

        internal void Shutdown()
        {
            _isShutdown = true;
            int ownerTotal = _ownerNextIndex;
            for (int i = 0; i < ownerTotal; i++)
            {
                if (!IsValidOwnerIndex(i))
                {
                    continue;
                }

                OwnerSlot owner = GetOwnerSlotRef(i);
                if (owner.State != 1)
                {
                    continue;
                }

                ReleaseOwner(owner.OwnerId, owner.Generation);
            }

            int bindingTotal = _bindingNextIndex;
            for (int i = 0; i < bindingTotal; i++)
            {
                ref BindingSlot binding = ref GetBindingSlotRef(i);
                if (!binding.Lease.IsValid)
                {
                    continue;
                }

                ClearAndReleaseBinding(ref binding);
            }

            _bindingIndexByOwnerSlot.Clear();
            _ownerIndexByGameObjectId.Clear();
            _ownerByTargetComponentId.Clear();
            _ownerPages = null;
            _bindingPages = null;
            _registeredTargetPages = null;
            _ownerNextIndex = 0;
            _bindingNextIndex = 0;
            _registeredTargetNextIndex = 0;
            _ownerFreeHead = -1;
            _bindingFreeHead = -1;
            _registeredTargetFreeHead = -1;
            _isShutdown = false;
        }

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

        private void ClearAndReleaseBinding(ref BindingSlot binding)
        {
            ClearKnownComponentSlot(ref binding);
            if (binding.RuntimeObject != null)
            {
                Object.Destroy(binding.RuntimeObject);
            }

            if (binding.Lease.IsValid)
            {
                _handler.Release(binding.Lease);
            }

            binding.Target = null;
            binding.AppliedAsset = null;
            binding.RuntimeObject = null;
            binding.Lease = ResourceLeaseHandle.Invalid;
            binding.AssetId = 0;
            binding.ViewKeyId = 0;
            binding.Flags = 0;
        }

        private static void ClearKnownComponentSlot(ref BindingSlot binding)
        {
            switch (binding.SlotType)
            {
                case EResourceBindingSlotType.ImageSprite:
                case EResourceBindingSlotType.SubSprite:
                    if (binding.Target is Image image && image.sprite == binding.AppliedAsset)
                    {
                        image.sprite = null;
                    }
                    break;
                case EResourceBindingSlotType.SpriteRendererSprite:
                    if (binding.Target is SpriteRenderer sr && sr.sprite == binding.AppliedAsset)
                    {
                        sr.sprite = null;
                    }
                    break;
                case EResourceBindingSlotType.ImageMaterial:
                    if (binding.Target is Image img && img.material == binding.AppliedAsset)
                    {
                        img.material = null;
                    }
                    break;
                case EResourceBindingSlotType.RendererSharedMaterial:
                    if (binding.Target is Renderer r && r.sharedMaterial == binding.AppliedAsset)
                    {
                        r.sharedMaterial = null;
                    }
                    break;
                case EResourceBindingSlotType.RendererMaterialInstance:
                    if (binding.Target is Renderer rr && rr.sharedMaterial == binding.RuntimeObject)
                    {
                        rr.sharedMaterial = null;
                    }
                    break;
            }
        }

        private void UnlinkBindingFromOwner(ref OwnerSlot owner, int bindingIndex)
        {
            int previous = -1;
            int current = owner.BindingHead;
            while (current >= 0)
            {
                ref BindingSlot binding = ref GetBindingSlotRef(current);
                int next = binding.NextByOwner;
                if (current == bindingIndex)
                {
                    if (previous >= 0)
                    {
                        ref BindingSlot prevBinding = ref GetBindingSlotRef(previous);
                        prevBinding.NextByOwner = next;
                    }
                    else
                    {
                        owner.BindingHead = next;
                    }

                    if (owner.BindingCount > 0)
                    {
                        owner.BindingCount--;
                    }

                    return;
                }

                previous = current;
                current = next;
            }
        }

        private void RemoveRegisteredTargetSlot(int ownerId, uint ownerGeneration, ulong targetComponentId)
        {
            int ownerIndex = ownerId - 1;
            if (!IsValidOwnerIndex(ownerIndex))
            {
                return;
            }

            ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
            if (owner.State != 1 || owner.Generation != ownerGeneration)
            {
                return;
            }

            int previous = -1;
            int current = owner.RegisteredTargetHead;
            while (current >= 0)
            {
                ref RegisteredTargetSlot target = ref GetRegisteredTargetSlotRef(current);
                int next = target.NextByOwner;
                if (target.TargetComponentId == targetComponentId &&
                    target.OwnerId == ownerId &&
                    target.OwnerGeneration == ownerGeneration)
                {
                    if (previous >= 0)
                    {
                        ref RegisteredTargetSlot prevTarget = ref GetRegisteredTargetSlotRef(previous);
                        prevTarget.NextByOwner = next;
                    }
                    else
                    {
                        owner.RegisteredTargetHead = next;
                    }

                    if (owner.RegisteredTargetCount > 0)
                    {
                        owner.RegisteredTargetCount--;
                    }

                    FreeRegisteredTargetSlot(current);
                    return;
                }

                previous = current;
                current = next;
            }
        }

        private static bool ApplySprite(Component target, Sprite sprite)
        {
            if (sprite == null || target == null)
            {
                return false;
            }

            switch (target)
            {
                case Image image:
                    image.sprite = sprite;
                    return true;

                case SpriteRenderer spriteRenderer:
                    spriteRenderer.sprite = sprite;
                    return true;

                default:
                    return false;
            }
        }

        private static bool ApplyMaterial(Component target, Material material)
        {
            if (material == null || target == null)
            {
                return false;
            }

            switch (target)
            {
                case Image image:
                    image.material = material;
                    return true;

                case Renderer renderer:
                    renderer.sharedMaterial = material;
                    return true;

                default:
                    return false;
            }
        }

        internal static void ClearMaterialSlot(Component target, Material appliedMaterial,
            Material runtimeMaterial, EResourceBindingSlotType slotType)
        {
            switch (slotType)
            {
                case EResourceBindingSlotType.ImageMaterial:
                    if (target is Image image && image.material == appliedMaterial)
                    {
                        image.material = null;
                    }
                    break;
                case EResourceBindingSlotType.RendererSharedMaterial:
                    if (target is Renderer r && r.sharedMaterial == appliedMaterial)
                    {
                        r.sharedMaterial = null;
                    }
                    break;
                case EResourceBindingSlotType.RendererMaterialInstance:
                    if (target is Renderer rr && rr.sharedMaterial == runtimeMaterial)
                    {
                        rr.sharedMaterial = null;
                    }
                    break;
            }
        }

        private static EResourceLeaseOption ToLeaseOptions(EResourceBindingOption options)
        {
            return (options & EResourceBindingOption.KeepAliveOnRelease) != 0
                ? EResourceLeaseOption.KeepAliveOnRelease
                : EResourceLeaseOption.None;
        }

        #endregion

        #region 分页 Slot 分配 [PAGED SLOT ALLOCATION]

        private int AllocateOwnerSlot()
        {
            int index;
            if (_ownerFreeHead >= 0)
            {
                index = _ownerFreeHead;
                ref OwnerSlot free = ref GetOwnerSlotRef(index);
                _ownerFreeHead = free.NextFree;
            }
            else
            {
                index = _ownerNextIndex++;
                EnsureOwnerPage(index);
            }

            ref OwnerSlot slot = ref GetOwnerSlotRef(index);
            uint generation = slot.Generation + 1;
            if (generation == 0)
            {
                generation = 1;
            }

            slot = default;
            slot.Generation = generation;
            slot.BindingHead = -1;
            slot.RegisteredTargetHead = -1;
            slot.NextFree = -1;
            return index;
        }

        private void FreeOwnerSlot(int index)
        {
            ref OwnerSlot slot = ref GetOwnerSlotRef(index);
            uint generation = slot.Generation;
            slot = default;
            slot.Generation = generation;
            slot.State = 0;
            slot.NextFree = _ownerFreeHead;
            _ownerFreeHead = index;
        }

        private int AllocateBindingSlot()
        {
            int index;
            if (_bindingFreeHead >= 0)
            {
                index = _bindingFreeHead;
                ref BindingSlot free = ref GetBindingSlotRef(index);
                _bindingFreeHead = free.NextFree;
            }
            else
            {
                index = _bindingNextIndex++;
                EnsureBindingPage(index);
            }

            ref BindingSlot slot = ref GetBindingSlotRef(index);
            slot = default;
            slot.NextByOwner = -1;
            slot.NextFree = -1;
            slot.Lease = ResourceLeaseHandle.Invalid;
            return index;
        }

        private void FreeBindingSlot(int index)
        {
            ref BindingSlot slot = ref GetBindingSlotRef(index);
            slot = default;
            slot.Lease = ResourceLeaseHandle.Invalid;
            slot.NextFree = _bindingFreeHead;
            _bindingFreeHead = index;
        }

        private int AllocateRegisteredTargetSlot()
        {
            int index;
            if (_registeredTargetFreeHead >= 0)
            {
                index = _registeredTargetFreeHead;
                ref RegisteredTargetSlot free = ref GetRegisteredTargetSlotRef(index);
                _registeredTargetFreeHead = free.NextFree;
            }
            else
            {
                index = _registeredTargetNextIndex++;
                EnsureRegisteredTargetPage(index);
            }

            ref RegisteredTargetSlot slot = ref GetRegisteredTargetSlotRef(index);
            slot = default;
            slot.NextByOwner = -1;
            slot.NextFree = -1;
            return index;
        }

        private void FreeRegisteredTargetSlot(int index)
        {
            ref RegisteredTargetSlot slot = ref GetRegisteredTargetSlotRef(index);
            slot = default;
            slot.NextFree = _registeredTargetFreeHead;
            _registeredTargetFreeHead = index;
        }

        private bool IsValidOwnerIndex(int index)
        {
            return index >= 0 && index < _ownerNextIndex && _ownerPages != null &&
                   _ownerPages[index >> PAGE_BITS] != null;
        }

        private ref OwnerSlot GetOwnerSlotRef(int index)
        {
            return ref _ownerPages[index >> PAGE_BITS][index & PAGE_MASK];
        }

        private ref BindingSlot GetBindingSlotRef(int index)
        {
            return ref _bindingPages[index >> PAGE_BITS][index & PAGE_MASK];
        }

        private ref RegisteredTargetSlot GetRegisteredTargetSlotRef(int index)
        {
            return ref _registeredTargetPages[index >> PAGE_BITS][index & PAGE_MASK];
        }

        private void EnsureOwnerPage(int index)
        {
            int page = index >> PAGE_BITS;
            if (_ownerPages == null)
            {
                _ownerPages = new OwnerSlot[Math.Max(4, page + 1)][];
            }
            else if (page >= _ownerPages.Length)
            {
                System.Array.Resize(ref _ownerPages, Math.Max(page + 1, _ownerPages.Length << 1));
            }

            if (_ownerPages[page] == null)
            {
                _ownerPages[page] = new OwnerSlot[PAGE_SIZE];
            }
        }

        private void EnsureBindingPage(int index)
        {
            int page = index >> PAGE_BITS;
            if (_bindingPages == null)
            {
                _bindingPages = new BindingSlot[Math.Max(4, page + 1)][];
            }
            else if (page >= _bindingPages.Length)
            {
                System.Array.Resize(ref _bindingPages, Math.Max(page + 1, _bindingPages.Length << 1));
            }

            if (_bindingPages[page] == null)
            {
                _bindingPages[page] = new BindingSlot[PAGE_SIZE];
            }
        }

        private void EnsureRegisteredTargetPage(int index)
        {
            int page = index >> PAGE_BITS;
            if (_registeredTargetPages == null)
            {
                _registeredTargetPages = new RegisteredTargetSlot[Math.Max(4, page + 1)][];
            }
            else if (page >= _registeredTargetPages.Length)
            {
                System.Array.Resize(ref _registeredTargetPages,
                    Math.Max(page + 1, _registeredTargetPages.Length << 1));
            }

            if (_registeredTargetPages[page] == null)
            {
                _registeredTargetPages[page] = new RegisteredTargetSlot[PAGE_SIZE];
            }
        }

        #endregion
    }
}
