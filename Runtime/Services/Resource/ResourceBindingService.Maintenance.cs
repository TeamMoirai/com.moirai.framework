using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 关停与重置——槽位排空、销毁态所有者与绑定的轮转回收、异常按项隔离后汇总重抛。
    /// </summary>
    partial class ResourceBindingService
    {
        /// <summary>
        /// 终态关停：排空所有所有者与绑定后**保持关闭位**，此后的注册与绑定一律以
        /// <see cref="EResourceBindStatus.ServiceShutdown"/> 拒绝。
        /// <para>槽位页已整体释放，若放行就会把新注册写进 <c>null</c> 页表。</para>
        /// </summary>
        internal void Shutdown()
        {
            _isShutdown = true;
            ReleaseAllSlots();
        }

        /// <summary>
        /// 重置：排空同 <see cref="Shutdown"/>，但完成后放行新的注册——供强制回收全部资源时
        /// 复用同一实例的路径调用。
        /// </summary>
        internal void Reset()
        {
            _isShutdown = true;
            try
            {
                ReleaseAllSlots();
            }
            finally
            {
                _isShutdown = false;
            }
        }

        /// <summary>
        /// 逐个所有者、逐条绑定地排空槽位，异常按项隔离后汇总重抛。
        /// </summary>
        private void ReleaseAllSlots()
        {
            List<Exception> exceptions = null;

            try
            {
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

                    // 单个所有者抛出不得截断其余所有者，否则其槽位与租约一路留到进程结束。
                    try
                    {
                        Internal_ReleaseOwner(owner.OwnerId, owner.Generation);
                    }
                    catch (Exception exception)
                    {
                        CollectException(ref exceptions, exception);
                    }
                }

                int bindingTotal = _bindingNextIndex;
                for (int i = 0; i < bindingTotal; i++)
                {
                    ref BindingSlot binding = ref GetBindingSlotRef(i);
                    if (!binding.Lease.IsValid)
                    {
                        continue;
                    }

                    try
                    {
                        ClearAndReleaseBinding(ref binding);
                    }
                    catch (Exception exception)
                    {
                        CollectException(ref exceptions, exception);
                    }
                }
            }
            finally
            {
                _bindingIndexByOwnerSlot.Clear();
                _ownerIndexByGameObjectId.Clear();
                _ownerPages = null;
                _bindingPages = null;
                _ownerNextIndex = 0;
                _bindingNextIndex = 0;
                _ownerFreeHead = -1;
                _bindingFreeHead = -1;
                _ownerSweepCursor = 0;
                _bindingSweepCursor = 0;
            }

            RethrowCollected(exceptions);
        }

        private static void CollectException(ref List<Exception> exceptions, Exception exception)
        {
            exceptions ??= new List<Exception>();
            // 摊平一层：内层收尾已把单条异常包成 AggregateException 上抛，外层再整只收进来
            // 就成了 AggregateException(AggregateException(...))，排查时要 Flatten() 才看得到根因。
            if (exception is AggregateException aggregate)
            {
                for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
                {
                    exceptions.Add(aggregate.InnerExceptions[i]);
                }

                return;
            }

            exceptions.Add(exception);
        }

        private static void RethrowCollected(List<Exception> exceptions)
        {
            if (exceptions == null)
            {
                return;
            }

            Exception error = exceptions.Count == 1 ? exceptions[0] : new AggregateException(exceptions);
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        /// <summary>
        /// 按预算轮转扫描所有者与绑定槽位，回收"Unity 对象已销毁、但 <c>OnDestroy</c> 没把账收走"的那部分。
        /// <para>典型现场是场景卸载与退出播放：销毁派发被截断后，槽位连同其租约会一路留到进程结束。</para>
        /// </summary>
        /// <param name="budget">本帧两类槽位各可查验的数量。刻意不给默认值：调用方一律显式传，
        /// 才能让"这个配额没人调"在编译期就暴露出来，而不是悄悄沿用一个常量。</param>
        internal void ProcessDestroyedObjects(int budget)
        {
            if (_isShutdown || budget <= 0 || _ownerPages == null)
            {
                return;
            }

            List<Exception> exceptions = null;

            int ownerTotal = _ownerNextIndex;
            for (int i = Math.Min(budget, ownerTotal); i > 0; i--)
            {
                if (_ownerSweepCursor >= ownerTotal)
                {
                    _ownerSweepCursor = 0;
                }

                int index = _ownerSweepCursor++;
                ref OwnerSlot slot = ref GetOwnerSlotRef(index);
                if (slot.State != 1 || !IsDestroyed(slot.Owner))
                {
                    continue;
                }

                try
                {
                    Internal_ReleaseOwner(slot.OwnerId, slot.Generation);
                }
                catch (Exception exception)
                {
                    CollectException(ref exceptions, exception);
                }
            }

            int bindingTotal = _bindingNextIndex;
            for (int i = Math.Min(budget, bindingTotal); i > 0; i--)
            {
                if (_bindingSweepCursor >= bindingTotal)
                {
                    _bindingSweepCursor = 0;
                }

                int index = _bindingSweepCursor++;
                ref BindingSlot binding = ref GetBindingSlotRef(index);
                // 判据只有一条：目标是否已销毁。已释放的槽位 Target 为空，天然到不了下面。
                // 刻意不再附加"有没有租约/资源"那层判据——异步预约留下的槽位正是
                // "有目标、有版本号、无租约无资源"的形状，按那个形状跳过它就永远轮不到回收，
                // 只在所有者释放时才走掉；而它占着 _bindingIndexByOwnerSlot 里的一条映射与一个版本号。
                if (!IsDestroyed(binding.Target))
                {
                    continue;
                }

                int ownerId = binding.OwnerId;
                uint ownerGeneration = binding.OwnerGeneration;
                BindingSlotKey slotKey = binding.SlotKey;

                try
                {
                    ClearAndReleaseBinding(ref binding);
                }
                catch (Exception exception)
                {
                    CollectException(ref exceptions, exception);
                }
                finally
                {
                    // 摘槽位是无条件项，不能被清理那一步的抛出截断：游标在判定之前就已推进，
                    // 漏摘一回，下一圈就撞回同一个槽位、再抛一次，直到进程结束。
                    ReleaseDestroyedBinding(index, ownerId, ownerGeneration, slotKey);
                }
            }

            RethrowCollected(exceptions);
        }

        private void ReleaseDestroyedBinding(int bindingIndex, int ownerId, uint ownerGeneration,
            BindingSlotKey slotKey)
        {
            int ownerIndex = ownerId - 1;
            if (IsValidOwnerIndex(ownerIndex))
            {
                ref OwnerSlot owner = ref GetOwnerSlotRef(ownerIndex);
                if (owner.State == 1 && owner.Generation == ownerGeneration)
                {
                    UnlinkBindingFromOwner(ref owner, bindingIndex);
                }
            }

            OwnerSlotKey key = new OwnerSlotKey(ownerId, slotKey);
            if (_bindingIndexByOwnerSlot.TryGetValue(key, out int mappedIndex) && mappedIndex == bindingIndex)
            {
                _bindingIndexByOwnerSlot.Remove(key);
            }

            FreeBindingSlot(bindingIndex);
        }

        // Unity 的 fake null：组件已被引擎销毁但 C# 引用仍在，== null 为真而 ReferenceEquals 为假
        private static bool IsDestroyed(UnityEngine.Object value)
        {
            return !ReferenceEquals(value, null) && value == null;
        }
    }
}
