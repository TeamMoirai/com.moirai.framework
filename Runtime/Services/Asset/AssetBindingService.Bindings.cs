using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Asset
{
    /// <summary>
    /// 绑定注册与应用——Sprite / Material / SubSprite 绑定与 PrefabSource 槽位落地，含组件槽位的清理与复原。
    /// </summary>
    partial class AssetBindingService
    {
        #region IAssetBindingService 实现 [IMPLEMENTATION]

        /// <inheritdoc />
        public EAssetBindStatus BindSprite(AssetOwner owner, Image image, AssetKey key,
            EAssetBindingOption options = EAssetBindingOption.None)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (image == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            AssetKey spriteKey = key.AssetType == null
                ? new AssetKey(key.Location, key.PackageName, typeof(Sprite), EAssetKind.Sprite)
                : key;
            AssetLeaseHandle newLease = _leaseSource.AcquireBinding(spriteKey);
            if (!newLease.IsValid)
            {
                return EAssetBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Sprite sprite)
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.LoadFailed;
            }

            if (!ApplySprite(image, sprite))
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, image, newLease, sprite, EAssetBindingSlotType.ImageSprite, options, 0);
        }

        /// <summary>
        /// 只读缓存绑定 Image 精灵：资源未加载则返回 <see cref="EAssetBindStatus.LoadFailed"/>，绝不触发后端加载。
        /// </summary>
        public EAssetBindStatus TryBindSpriteCached(AssetOwner owner, Image image, AssetKey key,
            EAssetBindingOption options = EAssetBindingOption.None)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int _);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (image == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            AssetKey spriteKey = key.AssetType == null
                ? new AssetKey(key.Location, key.PackageName, typeof(Sprite), EAssetKind.Sprite)
                : key;
            if (!_leaseSource.TryAcquireBindingCached(spriteKey, out AssetLeaseHandle newLease) || !newLease.IsValid)
            {
                return EAssetBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Sprite sprite)
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.LoadFailed;
            }

            if (!ApplySprite(image, sprite))
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, image, newLease, sprite, EAssetBindingSlotType.ImageSprite, options, 0);
        }

        /// <inheritdoc />
        public EAssetBindStatus BindSprite(AssetOwner owner, SpriteRenderer spriteRenderer, AssetKey key,
            EAssetBindingOption options = EAssetBindingOption.None)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (spriteRenderer == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            AssetKey spriteKey = key.AssetType == null
                ? new AssetKey(key.Location, key.PackageName, typeof(Sprite), EAssetKind.Sprite)
                : key;
            AssetLeaseHandle newLease = _leaseSource.AcquireBinding(spriteKey);
            if (!newLease.IsValid)
            {
                return EAssetBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Sprite sprite)
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.LoadFailed;
            }

            if (!ApplySprite(spriteRenderer, sprite))
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, spriteRenderer, newLease, sprite,
                EAssetBindingSlotType.SpriteRendererSprite, options, 0);
        }

        /// <inheritdoc />
        public UniTask<EAssetBindStatus> BindSubSpriteAsync(AssetOwner owner, Image image,
            AssetKey atlasKey, string spriteName, EAssetBindingOption options = EAssetBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            if (image == null)
            {
                return UniTask.FromResult(EAssetBindStatus.MissingTarget);
            }

            // SetNativeSize 由 RegisterSpriteSource 统一处理。
            // 与 SpriteRenderer 孪生一致直接转发：这里的 async/await 只是多垫一层状态机，
            // 底下返回的本来就是 UniTask。
            return BindSubSpriteSourceAsync(owner, image, atlasKey, spriteName,
                EAssetBindingSlotType.SubSprite, options, cancellationToken);
        }

        /// <inheritdoc />
        public UniTask<EAssetBindStatus> BindSubSpriteAsync(AssetOwner owner, SpriteRenderer spriteRenderer,
            AssetKey atlasKey, string spriteName, EAssetBindingOption options = EAssetBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            if (spriteRenderer == null)
            {
                return UniTask.FromResult(EAssetBindStatus.MissingTarget);
            }

            return BindSubSpriteSourceAsync(owner, spriteRenderer, atlasKey, spriteName,
                EAssetBindingSlotType.SpriteRendererSprite, options, cancellationToken);
        }

        private async UniTask<EAssetBindStatus> BindSubSpriteSourceAsync(AssetOwner owner, Component target,
            AssetKey atlasKey, string spriteName, EAssetBindingSlotType slotType,
            EAssetBindingOption options, CancellationToken cancellationToken)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            EAssetBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, target, slotType,
                out int ownerId, out uint ownerGeneration, out ulong targetComponentId,
                out ulong targetGameObjectId, out BindingSlotKey slotKey, out uint requestVersion);
            if (reserveStatus != EAssetBindStatus.Success)
            {
                return reserveStatus;
            }

            // 取用抛出时必须先取消预约位：后端未实现该异步能力时无条件抛（见 addressables 插件提供的实验性后端），
            // 而抛出的预约位形状是"有目标、有版本号、无租约无资源"——除所有者释放外无人再收它。
            AssetLeaseHandle newLease;
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
                return EAssetBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, target))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.StaleOwner;
            }

            if (!_leaseSource.TryGetSubSpriteAsset(newLease, spriteName, out Sprite sprite))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, target))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EAssetBindStatus.Cancelled
                    : EAssetBindStatus.StaleOwner;
            }

            if (!ApplySprite(target, sprite))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.ApplyFailed;
            }

            return RegisterSpriteSource(owner, target, newLease, sprite, slotType, options, requestVersion);
        }

        /// <inheritdoc />
        public EAssetBindStatus BindImageMaterial(AssetOwner owner, Image image, AssetKey key,
            EAssetBindingOption options = EAssetBindingOption.None)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int _);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (image == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            AssetKey materialKey = key.AssetType == null
                ? new AssetKey(key.Location, key.PackageName, typeof(Material), EAssetKind.Material)
                : key;
            AssetLeaseHandle newLease = _leaseSource.AcquireBinding(materialKey);
            if (!newLease.IsValid)
            {
                return EAssetBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material mat)
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.LoadFailed;
            }

            if (!ApplyMaterial(image, mat))
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.ApplyFailed;
            }

            return RegisterMaterialSource(owner, image, newLease, mat, null,
                EAssetBindingSlotType.ImageMaterial, options, 0);
        }

        /// <inheritdoc />
        public async UniTask<EAssetBindStatus> BindImageMaterialAsync(AssetOwner owner, Image image,
            AssetKey key, EAssetBindingOption options = EAssetBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (image == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            EAssetBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, image,
                EAssetBindingSlotType.ImageMaterial, out int ownerId, out uint ownerGeneration,
                out ulong targetComponentId, out ulong targetGameObjectId, out BindingSlotKey slotKey,
                out uint requestVersion);
            if (reserveStatus != EAssetBindStatus.Success)
            {
                return reserveStatus;
            }

            AssetKey materialKey = key.AssetType == null
                ? new AssetKey(key.Location, key.PackageName, typeof(Material), EAssetKind.Material)
                : key;
            AssetLeaseHandle newLease;
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
                return EAssetBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, image))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.StaleOwner;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material mat)
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, image))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EAssetBindStatus.Cancelled
                    : EAssetBindStatus.StaleOwner;
            }

            if (!ApplyMaterial(image, mat))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.ApplyFailed;
            }

            return RegisterMaterialSource(owner, image, newLease, mat, null,
                EAssetBindingSlotType.ImageMaterial, options, requestVersion);
        }

        /// <inheritdoc />
        public EAssetBindStatus BindSharedMaterial(AssetOwner owner, Renderer renderer, AssetKey key,
            EAssetBindingOption options = EAssetBindingOption.None)
        {
            return BindMaterialInternal(owner, renderer, key, false, options);
        }

        /// <inheritdoc />
        public UniTask<EAssetBindStatus> BindSharedMaterialAsync(AssetOwner owner, Renderer renderer,
            AssetKey key, EAssetBindingOption options = EAssetBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            return BindMaterialAsyncInternal(owner, renderer, key, false, options, cancellationToken);
        }

        /// <inheritdoc />
        public EAssetBindStatus BindMaterialInstance(AssetOwner owner, Renderer renderer, AssetKey key,
            EAssetBindingOption options = EAssetBindingOption.None)
        {
            return BindMaterialInternal(owner, renderer, key, true, options);
        }

        /// <inheritdoc />
        public UniTask<EAssetBindStatus> BindMaterialInstanceAsync(AssetOwner owner, Renderer renderer,
            AssetKey key, EAssetBindingOption options = EAssetBindingOption.None,
            CancellationToken cancellationToken = default)
        {
            return BindMaterialAsyncInternal(owner, renderer, key, true, options, cancellationToken);
        }


        #endregion
        #region 内部注册方法 [INTERNAL REGISTRATION]

        internal EAssetBindStatus RegisterPrefabSource(AssetOwner owner, AssetLeaseHandle lease,
            UObject prefabSource)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            BindingSlotKey slotKey = new BindingSlotKey(ownerSlot.GameObjectId,
                EAssetBindingSlotType.PrefabSource);
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
            AssetLeaseHandle oldLease = binding.Lease;
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
            binding.SlotType = EAssetBindingSlotType.PrefabSource;
            binding.Flags = (byte)EAssetBindingOption.KeepAliveOnRelease;
            _leaseSource.SetLeaseOptions(lease, EAssetLeaseOption.KeepAliveOnRelease);
            binding.Version++;
            if (oldLease.IsValid)
            {
                _leaseSource.Release(oldLease);
            }

            return EAssetBindStatus.Success;
        }

        // 5 参重载是清扫测试造绑定数据的接缝，生产路径一律走下面的完整重载。
        internal EAssetBindStatus RegisterSpriteSource(AssetOwner owner, Component target,
            AssetLeaseHandle lease, Sprite sprite, EAssetBindingSlotType slotType)
        {
            return RegisterSpriteSource(owner, target, lease, sprite, slotType, EAssetBindingOption.None, 0);
        }

        private EAssetBindStatus RegisterSpriteSource(AssetOwner owner, Component target,
            AssetLeaseHandle lease, Sprite sprite, EAssetBindingSlotType slotType,
            EAssetBindingOption options, uint reservedVersion)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            if (!lease.IsValid || sprite == null)
            {
                return EAssetBindStatus.InvalidKey;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            // 组件 id 在这里取一次、下面写槽位时复用：UnityObjectId.Get 每次都是一趟 managed→native 往返。
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
            AssetLeaseHandle oldLease = binding.Lease;
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

            if ((options & EAssetBindingOption.SetNativeSize) != 0 && target is Image img)
            {
                img.SetNativeSize();
            }

            return EAssetBindStatus.Success;
        }

        private EAssetBindStatus RegisterMaterialSource(AssetOwner owner, Component target,
            AssetLeaseHandle lease, Material appliedMaterial, Material runtimeMaterial,
            EAssetBindingSlotType slotType, EAssetBindingOption options, uint reservedVersion)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (target == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            if (!lease.IsValid || appliedMaterial == null)
            {
                return EAssetBindStatus.InvalidKey;
            }

            ref OwnerSlot ownerSlot = ref GetOwnerSlotRef(ownerIndex);
            // 同 RegisterSpriteSource：组件 id 取一次复用，避免多一趟 managed→native 往返。
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
            AssetLeaseHandle oldLease = binding.Lease;
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

            return EAssetBindStatus.Success;
        }


        #endregion
        #region 材质绑定内部 [MATERIAL BIND INTERNALS]

        private EAssetBindStatus BindMaterialInternal(AssetOwner owner, Renderer renderer, AssetKey key,
            bool createRuntimeInstance, EAssetBindingOption options)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int _);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (renderer == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            AssetKey materialKey = key.AssetType == null
                ? new AssetKey(key.Location, key.PackageName, typeof(Material), EAssetKind.Material)
                : key;
            AssetLeaseHandle newLease = _leaseSource.AcquireBinding(materialKey);
            if (!newLease.IsValid)
            {
                return EAssetBindStatus.LoadFailed;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material sourceMat)
            {
                _leaseSource.Release(newLease);
                return EAssetBindStatus.LoadFailed;
            }

            Material appliedMaterial = sourceMat;
            Material runtimeMaterial = null;
            EAssetBindingSlotType slotType = createRuntimeInstance
                ? EAssetBindingSlotType.RendererMaterialInstance
                : EAssetBindingSlotType.RendererSharedMaterial;

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
                return EAssetBindStatus.ApplyFailed;
            }

            EAssetBindStatus registerStatus = RegisterMaterialSource(owner, renderer, newLease,
                appliedMaterial, runtimeMaterial, slotType, options, 0);
            if (registerStatus != EAssetBindStatus.Success)
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

        private async UniTask<EAssetBindStatus> BindMaterialAsyncInternal(AssetOwner owner, Renderer renderer,
            AssetKey key, bool createRuntimeInstance, EAssetBindingOption options,
            CancellationToken cancellationToken)
        {
            EAssetBindStatus status = EnsureOwner(owner, out int ownerIndex);
            if (status != EAssetBindStatus.Success)
            {
                return status;
            }

            if (renderer == null)
            {
                return EAssetBindStatus.MissingTarget;
            }

            EAssetBindingSlotType slotType = createRuntimeInstance
                ? EAssetBindingSlotType.RendererMaterialInstance
                : EAssetBindingSlotType.RendererSharedMaterial;

            EAssetBindStatus reserveStatus = ReserveBindingRequest(ownerIndex, renderer, slotType,
                out int ownerId, out uint ownerGeneration, out ulong targetComponentId,
                out ulong targetGameObjectId, out BindingSlotKey slotKey, out uint requestVersion);
            if (reserveStatus != EAssetBindStatus.Success)
            {
                return reserveStatus;
            }

            AssetKey materialKey = key.AssetType == null
                ? new AssetKey(key.Location, key.PackageName, typeof(Material), EAssetKind.Material)
                : key;
            AssetLeaseHandle newLease;
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
                return EAssetBindStatus.LoadFailed;
            }

            if (!IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.StaleOwner;
            }

            if (!_leaseSource.TryGetLeaseAsset(newLease, out UObject asset) || asset is not Material sourceMat)
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.LoadFailed;
            }

            if (cancellationToken.IsCancellationRequested ||
                !IsBindingRequestCurrent(ownerId, ownerGeneration, targetComponentId, targetGameObjectId,
                    slotKey, requestVersion, renderer))
            {
                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return cancellationToken.IsCancellationRequested
                    ? EAssetBindStatus.Cancelled
                    : EAssetBindStatus.StaleOwner;
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
                return EAssetBindStatus.StaleOwner;
            }

            if (!ApplyMaterial(renderer, appliedMaterial))
            {
                if (runtimeMaterial != null)
                {
                    UObject.Destroy(runtimeMaterial);
                }

                _leaseSource.Release(newLease);
                CancelReservedBindingRequest(ownerId, ownerGeneration, slotKey, requestVersion);
                return EAssetBindStatus.ApplyFailed;
            }

            EAssetBindStatus registerStatus = RegisterMaterialSource(owner, renderer, newLease,
                appliedMaterial, runtimeMaterial, slotType, options, requestVersion);
            if (registerStatus != EAssetBindStatus.Success)
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
            EAssetBindingSlotType slotType = binding.SlotType;
            AssetLeaseHandle lease = binding.Lease;

            binding.Target = null;
            binding.AppliedAsset = null;
            binding.RuntimeObject = null;
            binding.Lease = AssetLeaseHandle.Invalid;
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
            UObject runtimeObject, EAssetBindingSlotType slotType)
        {
            switch (slotType)
            {
                case EAssetBindingSlotType.ImageSprite:
                case EAssetBindingSlotType.SubSprite:
                    if (target is Image image && image.sprite == appliedAsset)
                    {
                        image.sprite = null;
                    }
                    break;
                case EAssetBindingSlotType.SpriteRendererSprite:
                    if (target is SpriteRenderer sr && sr.sprite == appliedAsset)
                    {
                        sr.sprite = null;
                    }
                    break;
                case EAssetBindingSlotType.ImageMaterial:
                    if (target is Image img && img.material == appliedAsset)
                    {
                        img.material = null;
                    }
                    break;
                case EAssetBindingSlotType.RendererSharedMaterial:
                    if (target is Renderer r && r.sharedMaterial == appliedAsset)
                    {
                        r.sharedMaterial = null;
                    }
                    break;
                case EAssetBindingSlotType.RendererMaterialInstance:
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
            Material runtimeMaterial, EAssetBindingSlotType slotType)
        {
            switch (slotType)
            {
                case EAssetBindingSlotType.ImageMaterial:
                    if (target is Image image && image.material == appliedMaterial)
                    {
                        image.material = null;
                    }
                    break;
                case EAssetBindingSlotType.RendererSharedMaterial:
                    if (target is Renderer r && r.sharedMaterial == appliedMaterial)
                    {
                        r.sharedMaterial = null;
                    }
                    break;
                case EAssetBindingSlotType.RendererMaterialInstance:
                    if (target is Renderer rr && rr.sharedMaterial == runtimeMaterial)
                    {
                        rr.sharedMaterial = null;
                    }
                    break;
            }
        }

        private static EAssetLeaseOption ToLeaseOptions(EAssetBindingOption options)
        {
            return (options & EAssetBindingOption.KeepAliveOnRelease) != 0
                ? EAssetLeaseOption.KeepAliveOnRelease
                : EAssetLeaseOption.None;
        }

        #endregion
    }
}
