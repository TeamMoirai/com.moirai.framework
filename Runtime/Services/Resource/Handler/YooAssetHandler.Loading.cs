using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 加载核心与去重——赢家/等待者路径、进度回调、卸载代际校验与在途操作闭环。
    /// </summary>
    partial class YooAssetHandler
    {
        #region 资源加载核心 [CORE ASSET LOADING]

        private UObject GetOrLoadAsset(string location, Type assetType, EResourceAssetKind assetKind,
            string packageName)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);
            ulong loadingKey = GetLoadingOperationKey(location, normalizedPackageName, assetType, assetKind);

            while (true)
            {
                if (_isDestroying)
                {
                    return null;
                }

                if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out UObject cachedAsset))
                {
                    return cachedAsset;
                }

                if (!TryBeginLoading(loadingKey))
                {
                    AssetHandle joinHandle = GetHandleSync(location, assetType, packageName);
                    if (joinHandle == null || joinHandle.AssetObject == null ||
                        joinHandle.Status == EOperationStatus.Failed)
                    {
                        DisposeHandle(joinHandle);
                        return null;
                    }

                    GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, joinHandle.AssetObject, joinHandle);
                    return TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out cachedAsset)
                        ? cachedAsset
                        : null;
                }

                int loadGeneration = unchecked((int)_assetUnloadGeneration);
                AssetHandle handle = null;
                try
                {
                    if (!IsLoadingStateCurrent(loadGeneration))
                    {
                        FailLoading(loadingKey, null);
                        return null;
                    }

                    handle = GetHandleSync(location, assetType, packageName);
                    if (handle == null || handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
                    {
                        Exception failure = handle != null
                            ? NewLoadingFailure("Asset", location, normalizedPackageName, handle.Status, handle.Error)
                            : NewLoadingFailure("Asset", location, normalizedPackageName);
                        DisposeHandle(handle);
                        handle = null;
                        FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return null;
                    }

                    UObject loadedAsset = handle.AssetObject;
                    GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                    handle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    CompleteLoading(loadingKey);
                    return loadedAsset;
                }
                catch (Exception ex)
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource Asset sync load threw. Location:{0} Package:{1} Type:{2}", location,
                        normalizedPackageName, assetType), ex));
                    return null;
                }
            }
        }

        private async UniTask<UObject> GetOrLoadAssetAsync(string location, Type assetType,
            EResourceAssetKind assetKind, string packageName, ulong loadingKey,
            uint priority = 0, CancellationToken cancellationToken = default,
            LoadAssetUpdateCallback loadAssetUpdateCallback = null, object userData = null)
        {
            string normalizedPackageName = NormalizePackageName(packageName);
            assetKind = NormalizeAssetKind(assetType, assetKind);
            assetType = NormalizeAssetType(assetType, assetKind);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (_isDestroying)
                {
                    return null;
                }

                if (TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out UObject cachedAsset))
                {
                    return cachedAsset;
                }

                if (!TryBeginLoading(loadingKey))
                {
                    if (!await WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return null;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)_assetUnloadGeneration);
                AssetHandle handle = null;
                try
                {
                    if (!IsLoadingStateCurrent(loadGeneration))
                    {
                        FailLoading(loadingKey, null);
                        return null;
                    }

                    handle = GetHandleAsync(location, assetType, packageName: packageName, priority: priority);
                    if (handle == null)
                    {
                        FailLoading(loadingKey, NewLoadingFailure("Asset", location, normalizedPackageName),
                            ELogLevel.Warning);
                        return null;
                    }

                    AttachLoadingAssetHandle(loadingKey, handle);
                    StartProgressTask(location, handle, loadAssetUpdateCallback, userData, cancellationToken);
                    bool callerCancellationRequested = false;
                    if (!handle.IsDone)
                    {
                        await handle.ToUniTask(cancellationToken: cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        callerCancellationRequested = true;
                    }

                    if (!IsLoadingStateCurrent(loadGeneration))
                    {
                        DisposeHandle(handle);
                        handle = null;
                        FailLoading(loadingKey, null);
                        return null;
                    }

                    if (ShouldAbortLoadingAfterCallerCancellation(loadingKey, cancellationToken, ref callerCancellationRequested))
                    {
                        DisposeHandle(handle);
                        handle = null;
                        FailLoading(loadingKey, null);
                        return null;
                    }

                    if (!handle.IsValid || handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
                    {
                        Exception failure = NewLoadingFailure("Asset", location, normalizedPackageName, handle.Status,
                            handle.Error);
                        DisposeHandle(handle);
                        handle = null;
                        FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return null;
                    }

                    if (_isDestroying)
                    {
                        DisposeHandle(handle);
                        handle = null;
                        FailLoading(loadingKey, null);
                        return null;
                    }

                    GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                    handle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    CompleteLoading(loadingKey);
                    if (callerCancellationRequested)
                    {
                        return null;
                    }

                    return TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                            EResourceHandleKind.AssetHandle, out _, out cachedAsset)
                        ? cachedAsset
                        : null;
                }
                catch (OperationCanceledException)
                {
                    // 取消按契约吞成 null；去重槽必须闭环失败，否则同资源后续并发加载会在 WaitForLoadingAsync 里空转。
                    DisposeHandle(handle);
                    FailLoading(loadingKey, null);
                    return null;
                }
                catch (Exception ex)
                {
                    DisposeHandle(handle);
                    FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource Asset load threw. Location:{0} Package:{1} Type:{2}", location, normalizedPackageName,
                        assetType), ex));
                    return null;
                }
            }
        }

        private bool TryBeginLoading(ulong assetObjectKey)
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

        private void AttachLoadingAssetHandle(ulong assetObjectKey, AssetHandle handle)
        {
            if (TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                loadingOperation.AssetHandle = handle;
            }
        }

        private void AttachLoadingSubAssetsHandle(ulong assetObjectKey, SubAssetsHandle handle)
        {
            if (TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                loadingOperation.SubAssetsHandle = handle;
            }
        }

        private async UniTask<bool> WaitForLoadingAsync(ulong assetObjectKey,
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
                    if (cancellationToken.IsCancellationRequested || _isDestroying)
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

        private void CompleteLoading(ulong assetObjectKey)
        {
            if (!TryRemoveLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                return;
            }

            loadingOperation.Complete(true);
            loadingOperation.RequestRelease();
            ReleaseLoadingOperationIfReady(loadingOperation);
        }

        private void FailLoading(ulong assetObjectKey, Exception exception,
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

        private static GameException NewLoadingFailure(string operation, string location, string packageName)
        {
            return new GameException(StringUtility.Format(
                "Resource {0} load failed: handler returned null. Location:{1} Package:{2}",
                operation, location, packageName));
        }

        private static GameException NewLoadingFailure(string operation, string location, string packageName,
            EOperationStatus status, string error)
        {
            return new GameException(StringUtility.Format(
                "Resource {0} load failed. Location:{1} Package:{2} Status:{3} Error:{4}",
                operation, location, packageName, status.ToString(), error ?? string.Empty));
        }

        private bool TryGetLoadingOperation(ulong assetObjectKey, out LoadingOperationState loadingOperation)
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

        private bool IsLoadingStateCurrent(int loadGeneration)
        {
            return !_isDestroying && loadGeneration == unchecked((int)_assetUnloadGeneration);
        }

        private bool ShouldAbortLoadingAfterCallerCancellation(ulong assetObjectKey,
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

        private void ShutdownLoadingOperations()
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

        private void StartProgressTask(string location, AssetHandle handle,
            LoadAssetUpdateCallback loadAssetUpdateCallback, object userData, CancellationToken cancellationToken)
        {
            if (loadAssetUpdateCallback != null && handle is { IsValid: true, IsDone: false })
            {
                InvokeProgress(location, handle, loadAssetUpdateCallback, userData, cancellationToken).Forget();
            }
        }

        private async UniTaskVoid InvokeProgress(string location, AssetHandle assetHandle,
            LoadAssetUpdateCallback loadAssetUpdateCallback, object userData, CancellationToken cancellationToken)
        {
            if (loadAssetUpdateCallback != null)
            {
                float lastReportedProgress = -1f;
                while (assetHandle is { IsValid: true, IsDone: false })
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await UniTask.Yield();
                    float progress = assetHandle.Progress;
                    if (lastReportedProgress < 0f || progress - lastReportedProgress >= PROGRESS_CALLBACK_THRESHOLD)
                    {
                        lastReportedProgress = progress;
                        loadAssetUpdateCallback.Invoke(location, progress, userData);
                    }
                }

                if (!cancellationToken.IsCancellationRequested && assetHandle is { IsValid: true } &&
                    lastReportedProgress < 1f)
                {
                    loadAssetUpdateCallback.Invoke(location, 1f, userData);
                }
            }
        }

        private SubAssetsHandle GetSubAssetsHandleAsync(string location, string packageName)
        {
            return GetPackageOrThrow(packageName).LoadSubAssetsAsync<Sprite>(location);
        }

        #endregion
    }
}
