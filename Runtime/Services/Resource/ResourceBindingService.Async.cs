using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 异步绑定安全——预约请求位、取消预约，以及“结果是否仍属于当前绑定”的代次判定。
    /// </summary>
    partial class ResourceBindingService
    {
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
            slotKey = new BindingSlotKey(targetComponentId, slotType);
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
    }
}
