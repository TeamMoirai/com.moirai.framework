using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 绑定注册与应用——Sprite / Material / SubSprite 绑定与 PrefabSource 槽位落地，含组件槽位的清理与复原。
    /// </summary>
    partial class ResourceBindingService
    {
        #region IResourceBindingService 实现 [IMPLEMENTATION]

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
            ResourceLeaseHandle newLease = _leaseSource.AcquireBinding(spriteKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Sprite sprite)
            {
                _leaseSource.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            if (!ApplySprite(image, sprite))
            {
                _leaseSource.Release(newLease);
                return EResourceBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, image, newLease, sprite, EResourceBindingSlotType.ImageSprite, options, 0);
        }

        /// <summary>
        /// 只读缓存绑定 Image 精灵：资源未加载则返回 <see cref="EResourceBindStatus.LoadFailed"/>，绝不触发后端加载。
        /// </summary>
        public EResourceBindStatus TryBindSpriteCached(ResourceOwner owner, Image image, ResourceKey key,
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

            ResourceKey spriteKey = key.AssetType == null
                ? new ResourceKey(key.Location, key.PackageName, typeof(Sprite), EResourceAssetKind.Sprite)
                : key;
            if (!_leaseSource.TryAcquireBindingCached(spriteKey, out ResourceLeaseHandle newLease) || !newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Sprite sprite)
            {
                _leaseSource.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            if (!ApplySprite(image, sprite))
            {
                _leaseSource.Release(newLease);
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
            ResourceLeaseHandle newLease = _leaseSource.AcquireBinding(spriteKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Sprite sprite)
            {
                _leaseSource.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            if (!ApplySprite(spriteRenderer, sprite))
            {
                _leaseSource.Release(newLease);
                return EResourceBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, spriteRenderer, newLease, sprite,
                EResourceBindingSlotType.SpriteRendererSprite, options, 0);
        }

        /// <inheritdoc />
        public UniTask<EResourceBindStatus> BindSubSpriteAsync(ResourceOwner owner, Image image,
            ResourceKey atlasKey, string spriteName, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            if (image == null)
            {
                return UniTask.FromResult(EResourceBindStatus.MissingTarget);
            }

            // SetNativeSize 由 RegisterSpriteSource 统一处理。
            // 与 SpriteRenderer 孪生一致直接转发：这里的 async/await 只是多垫一层状态机，
            // 底下返回的本来就是 UniTask。
            return BindSubSpriteSourceAsync(owner, image, atlasKey, spriteName,
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

            // 取用抛出时必须先取消预约位：后端未实现该异步能力时无条件抛（见 AddressableHandler），
            // 而抛出的预约位形状是"有目标、有版本号、无租约无资源"——除所有者释放外无人再收它。
            ResourceLeaseHandle newLease;
            try
            {
                newLease = await _leaseSource.AcquireSubAssetsBindingAsync(
                    atlasKey.Location, atlasKey.PackageName,
                    ToLeaseOptions(options), cancellationToken);
            }
            catch
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                throw;
            }

            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, target))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!_leaseSource.TryGetSubSpriteAsset(newLease, spriteName, out Sprite sprite))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, target))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EResourceBindStatus.Cancelled
                    : EResourceBindStatus.StaleOwner;
            }

            if (!ApplySprite(target, sprite))
            {
                _leaseSource.Release(newLease);
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
            ResourceLeaseHandle newLease = _leaseSource.AcquireBinding(materialKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material mat)
            {
                _leaseSource.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            if (!ApplyMaterial(image, mat))
            {
                _leaseSource.Release(newLease);
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
            ResourceLeaseHandle newLease;
            try
            {
                newLease = await _leaseSource.AcquireBindingAsync(materialKey, cancellationToken);
            }
            catch
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                throw;
            }
            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, image))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material mat)
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, image))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EResourceBindStatus.Cancelled
                    : EResourceBindStatus.StaleOwner;
            }

            if (!ApplyMaterial(image, mat))
            {
                _leaseSource.Release(newLease);
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
            return BindMaterialInternal(owner, renderer, key, false, options);
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
            return BindMaterialInternal(owner, renderer, key, true, options);
        }

        /// <inheritdoc />
        public UniTask<EResourceBindStatus> BindMaterialInstanceAsync(ResourceOwner owner, Renderer renderer,
            ResourceKey key, EResourceBindingOption options = EResourceBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            return BindMaterialAsyncInternal(owner, renderer, key, true, options, cancellationToken);
        }


        #endregion
        #region 内部注册方法 [INTERNAL REGISTRATION]

        internal EResourceBindStatus RegisterPrefabSource(ResourceOwner owner, ResourceLeaseHandle lease,
            UObject prefabSource)
        {
            EResourceBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EResourceBindStatus.Success)
            {
                return status;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            BindingSlotKey slotKey = new BindingSlotKey(ownerSlot.GameObjectId,
                EResourceBindingSlotType.PrefabSource);
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
            binding.AssetId = _leaseSource.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.Lease = lease;
            binding.SlotType = EResourceBindingSlotType.PrefabSource;
            binding.Flags = (byte)EResourceBindingOption.KeepAliveOnRelease;
            _leaseSource.SetLeaseOptions(lease, EResourceLeaseOption.KeepAliveOnRelease);
            binding.Version++;
            if (oldLease.IsValid)
            {
                _leaseSource.Release(oldLease);
            }

            return EResourceBindStatus.Success;
        }

        // 5 参重载是清扫测试造绑定数据的接缝，生产路径一律走下面的完整重载。
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
            // 同一个目标的组件 id 原先在这里和下面各算一次，每次都是一趟 managed→native 往返。
            ulong targetComponentId = UnityObjectId.Get(target);
            BindingSlotKey slotKey = new BindingSlotKey(targetComponentId, slotType);
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
            binding.TargetComponentId = targetComponentId;
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = target;
            binding.AppliedAsset = sprite;
            binding.RuntimeObject = null;
            binding.AssetId = _leaseSource.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.Lease = lease;
            binding.SlotType = slotType;
            binding.Flags = (byte)options;
            _leaseSource.SetLeaseOptions(lease, ToLeaseOptions(options));
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
                _leaseSource.Release(oldLease);
            }

            if ((options & EResourceBindingOption.SetNativeSize) != 0 && target is Image img)
            {
                img.SetNativeSize();
            }

            return EResourceBindStatus.Success;
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
            // 同 RegisterSpriteSource：目标组件 id 原本在此与下面各算一次。
            ulong targetComponentId = UnityObjectId.Get(target);
            BindingSlotKey slotKey = new BindingSlotKey(targetComponentId, slotType);
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
            UObject oldRuntimeObject = binding.RuntimeObject;
            binding.SlotKey = slotKey;
            binding.OwnerId = ownerSlot.OwnerId;
            binding.TargetGameObjectId = UnityObjectId.Get(target.gameObject);
            binding.TargetComponentId = targetComponentId;
            binding.OwnerGeneration = ownerSlot.Generation;
            binding.Target = target;
            binding.AppliedAsset = appliedMaterial;
            binding.RuntimeObject = runtimeMaterial;
            binding.AssetId = _leaseSource.TryGetLeaseAssetId(lease, out int assetId) ? assetId : -1;
            binding.Lease = lease;
            binding.SlotType = slotType;
            binding.Flags = (byte)options;
            _leaseSource.SetLeaseOptions(lease, ToLeaseOptions(options));
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
                UObject.Destroy(oldRuntimeObject);
            }

            if (oldLease.IsValid)
            {
                _leaseSource.Release(oldLease);
            }

            return EResourceBindStatus.Success;
        }


        #endregion
        #region 材质绑定内部 [MATERIAL BIND INTERNALS]

        private EResourceBindStatus BindMaterialInternal(ResourceOwner owner, Renderer renderer, ResourceKey key,
            bool createRuntimeInstance, EResourceBindingOption options)
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
            ResourceLeaseHandle newLease = _leaseSource.AcquireBinding(materialKey);
            if (!newLease.IsValid)
            {
                return EResourceBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material sourceMat)
            {
                _leaseSource.Release(newLease);
                return EResourceBindStatus.LoadFailed;
            }

            Material appliedMaterial = sourceMat;
            Material runtimeMaterial = null;
            EResourceBindingSlotType slotType = createRuntimeInstance
                ? EResourceBindingSlotType.RendererMaterialInstance
                : EResourceBindingSlotType.RendererSharedMaterial;

            if (createRuntimeInstance)
            {
                runtimeMaterial = UObject.Instantiate(sourceMat);
                appliedMaterial = runtimeMaterial;
            }

            if (!ApplyMaterial(renderer, appliedMaterial))
            {
                if (runtimeMaterial != null)
                {
                    UObject.Destroy(runtimeMaterial);
                }

                _leaseSource.Release(newLease);
                return EResourceBindStatus.ApplyFailed;
            }

            EResourceBindStatus registerStatus = RegisterMaterialSource(owner, renderer, newLease,
                appliedMaterial, runtimeMaterial, slotType, options, 0);
            if (registerStatus != EResourceBindStatus.Success)
            {
                ClearMaterialSlot(renderer, appliedMaterial, runtimeMaterial, slotType);
                if (runtimeMaterial != null)
                {
                    UObject.Destroy(runtimeMaterial);
                }

                _leaseSource.Release(newLease);
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
            ResourceLeaseHandle newLease;
            try
            {
                newLease = await _leaseSource.AcquireBindingAsync(materialKey, cancellationToken);
            }
            catch
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                throw;
            }
            if (!newLease.IsValid)
            {
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material sourceMat)
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EResourceBindStatus.Cancelled
                    : EResourceBindStatus.StaleOwner;
            }

            Material appliedMaterial = sourceMat;
            Material runtimeMaterial = null;
            if (createRuntimeInstance)
            {
                runtimeMaterial = UObject.Instantiate(sourceMat);
                appliedMaterial = runtimeMaterial;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                if (runtimeMaterial != null)
                {
                    UObject.Destroy(runtimeMaterial);
                }

                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EResourceBindStatus.StaleOwner;
            }

            if (!ApplyMaterial(renderer, appliedMaterial))
            {
                if (runtimeMaterial != null)
                {
                    UObject.Destroy(runtimeMaterial);
                }

                _leaseSource.Release(newLease);
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
                    UObject.Destroy(runtimeMaterial);
                }

                _leaseSource.Release(newLease);
            }

            return registerStatus;
        }


        #endregion
        #region 辅助方法 [UTILITY METHODS]

        private void ClearAndReleaseBinding(ref BindingSlot binding)
        {
            // 记账先于表现：租约归还与槽位清空必须无条件完成，组件槽位清理则是尽力而为的一项。
            // 后者要在"引擎已销毁但托管引用仍在"的组件上读原生属性（SpriteRenderer.sprite 等），
            // 必然抛 MissingReferenceException——而那恰恰是本路径的正常现场，
            // 让它排在归还之前，赔进去的就是一条永不归还的租约。
            Component target = binding.Target;
            UObject appliedAsset = binding.AppliedAsset;
            UObject runtimeObject = binding.RuntimeObject;
            EResourceBindingSlotType slotType = binding.SlotType;
            ResourceLeaseHandle lease = binding.Lease;

            binding.Target = null;
            binding.AppliedAsset = null;
            binding.RuntimeObject = null;
            binding.Lease = ResourceLeaseHandle.Invalid;
            binding.AssetId = 0;
            binding.Flags = 0;

            if (lease.IsValid)
            {
                _leaseSource.Release(lease);
            }

            if (runtimeObject != null)
            {
                UObject.Destroy(runtimeObject);
            }

            // 目标已销毁时这一步必定抛出：槽位与租约上面已经收干净，不能再被它截断，也不该向外透——
            // 对已销毁对象清组件槽位本来就没有任何可成之事，它的失败不值一条异常。
            try
            {
                ClearKnownComponentSlot(target, appliedAsset, runtimeObject, slotType);
            }
            catch (System.Exception)
            {
                // 刻意不记日志：销毁态回收是常规路径，每帧逐条打只会把日志淹掉。
            }
        }

        private static void ClearKnownComponentSlot(Component target, UObject appliedAsset,
            UObject runtimeObject, EResourceBindingSlotType slotType)
        {
            switch (slotType)
            {
                case EResourceBindingSlotType.ImageSprite:
                case EResourceBindingSlotType.SubSprite:
                    if (target is Image image && image.sprite == appliedAsset)
                    {
                        image.sprite = null;
                    }
                    break;
                case EResourceBindingSlotType.SpriteRendererSprite:
                    if (target is SpriteRenderer sr && sr.sprite == appliedAsset)
                    {
                        sr.sprite = null;
                    }
                    break;
                case EResourceBindingSlotType.ImageMaterial:
                    if (target is Image img && img.material == appliedAsset)
                    {
                        img.material = null;
                    }
                    break;
                case EResourceBindingSlotType.RendererSharedMaterial:
                    if (target is Renderer r && r.sharedMaterial == appliedAsset)
                    {
                        r.sharedMaterial = null;
                    }
                    break;
                case EResourceBindingSlotType.RendererMaterialInstance:
                    if (target is Renderer rr && rr.sharedMaterial == runtimeObject)
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
    }
}
