using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 加载去重——同一条 packed key 的并发请求只发一次后端加载，其余在此排队等结果。
    /// <para>排队与成败只认计数，句柄本身由具体后端解释：本文件因此不出现任何后端类型，
    /// 把句柄写进槽的两个 Attach 留在后端侧。</para>
    /// </summary>
    internal sealed partial class ResourceRecordStore
    {
        internal bool TryBeginLoading(ulong assetObjectKey)
        {
            bool keyAlreadyRetained = false;
            if (_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int existingSlotIndex))
            {
                if (IsValidLoadingOperationSlotId(existingSlotIndex))
                {
                    ref LoadingOperationSlot existingSlot = ref GetLoadingOperationSlotRef(existingSlotIndex);
                    if (existingSlot.State == 1 && existingSlot.Key == assetObjectKey &&
                        existingSlot.Operation != null)
                    {
                        return false;
                    }
                }

                _assetLoadingOperationByKey.Remove(assetObjectKey);
                keyAlreadyRetained = true;
            }

            int slotIndex = AllocateLoadingOperationSlot();
            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            slot.Key = assetObjectKey;
            slot.Operation = MemoryPool.Acquire<LoadingOperationState>();
            slot.State = 1;
            _assetLoadingOperationByKey.Set(assetObjectKey, slotIndex);
            if (!keyAlreadyRetained)
            {
                RetainResourceKey(assetObjectKey);
            }

            return true;
        }
        internal async UniTask<bool> WaitForLoadingAsync(ulong assetObjectKey,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return true;
            }

            loadingOperation.AddWaiter();
            try
            {
                while (!loadingOperation.IsDone)
                {
                    if (cancellationToken.IsCancellationRequested || IsDestroying)
                    {
                        return false;
                    }

                    await UniTask.Yield();
                }

                return loadingOperation.Succeeded;
            }
            finally
            {
                // 等待者计数一旦泄漏，去重槽连同它持有的 AssetHandle 再也回不了池（map 条目已先摘除，泄漏是无声的）。
                loadingOperation.RemoveWaiter();
                ReleaseLoadingOperationIfReady(loadingOperation);
            }
        }
        internal void CompleteLoading(ulong assetObjectKey)
        {
            if (!TryRemoveLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return;
            }

            loadingOperation.Complete(true);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }
        internal void FailLoading(ulong assetObjectKey, Exception exception,
            ELogLevel level = ELogLevel.Error)
        {
            if (exception != null)
            {
                // 失败原因若在此被丢弃，等待方只会拿到 null 且日志里查不到加载为什么失败。
                // 分级对齐 RETHROW 约定：可预期的资源加载失败降为 Warning，意外异常仍以 Error 留痕。
                if (level == ELogLevel.Warning)
                {
                    LogUtility.Warning(exception);
                }
                else
                {
                    LogUtility.Error(exception);
                }
            }

            if (!TryRemoveLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return;
            }

            loadingOperation.Complete(false);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }
        internal bool TryGetLoadingOperation(ulong assetObjectKey, out LoadingOperationState loadingOperation)
        {
            loadingOperation = null;
            if (!_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int slotIndex) ||
                !IsValidLoadingOperationSlotId(slotIndex))
            {
                return false;
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            if (slot.State != 1 || slot.Key != assetObjectKey || slot.Operation == null)
            {
                return false;
            }

            loadingOperation = slot.Operation;
            return true;
        }
        private bool TryRemoveLoadingOperation(ulong assetObjectKey, out LoadingOperationState loadingOperation)
        {
            loadingOperation = null;
            if (!_assetLoadingOperationByKey.TryGetValue(assetObjectKey, out int slotIndex) ||
                !IsValidLoadingOperationSlotId(slotIndex))
            {
                return false;
            }

            ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(slotIndex);
            if (slot.State != 1 || slot.Key != assetObjectKey || slot.Operation == null)
            {
                _assetLoadingOperationByKey.Remove(assetObjectKey);
                ReleaseResourceKey(assetObjectKey);
                return false;
            }

            loadingOperation = slot.Operation;
            _assetLoadingOperationByKey.Remove(assetObjectKey);
            ReleaseResourceKey(assetObjectKey);
            FreeLoadingOperationSlot(slotIndex);
            return true;
        }
        private bool HasLoadingWaiters(ulong assetObjectKey)
        {
            return TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation) &&
                   loadingOperation.WaiterCount > 0;
        }
        internal bool IsLoadingStateCurrent(int loadGeneration)
        {
            return !IsDestroying && loadGeneration == unchecked((int)UnloadGeneration);
        }
        internal bool ShouldAbortLoadingAfterCallerCancellation(ulong assetObjectKey,
            CancellationToken cancellationToken, ref bool callerCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                callerCancellationRequested = true;
            }

            return callerCancellationRequested && !HasLoadingWaiters(assetObjectKey);
        }
        private static void ReleaseLoadingOperationIfReady(LoadingOperationState loadingOperation)
        {
            if (loadingOperation is { ReleaseRequested: true, WaiterCount: 0 })
            {
                MemoryPool.Release(loadingOperation);
            }
        }
        internal void ShutdownLoadingOperations()
        {
            int total = _loadingOperationSlotNextIndex;
            for (int i = 0; i < total; i++)
            {
                ref LoadingOperationSlot slot = ref GetLoadingOperationSlotRef(i);
                if (slot.State != 1 || slot.Operation == null)
                {
                    continue;
                }

                LoadingOperationState loadingOperation = slot.Operation;
                loadingOperation.Complete(false);
                loadingOperation.RequestRelease();
                ReleaseLoadingOperationIfReady(loadingOperation);
                ClearLoadingOperationSlot(ref slot);
            }

            ReleaseAllResourceKeysFromMap(_assetLoadingOperationByKey);
            _assetLoadingOperationByKey.Clear();
            _loadingOperationSlotPages = null;
            _loadingOperationSlotNextIndex = 0;
            _loadingOperationSlotFreeHead = -1;
        }
    }
}
