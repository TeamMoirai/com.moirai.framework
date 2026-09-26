using System;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 分页槽位分配——Owner / Binding 两类页表的借用、归还与页构造。
    /// </summary>
    partial class ResourceBindingService
    {
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
    }
}
