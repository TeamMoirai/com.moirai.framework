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
            string normalizedPackageName = Kernel.NormalizePackageName(packageName);
            assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, assetKind);
            assetType = ResourceKeyCodec.NormalizeAssetType(assetType, assetKind);
            ulong loadingKey = Kernel.GetLoadingOperationKey(location, normalizedPackageName, assetType, assetKind);

            while (true)
            {
                if (Kernel.IsDestroying)
                {
                    return null;
                }

                if (Kernel.TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out UObject cachedAsset))
                {
                    return cachedAsset;
                }

                if (!Kernel.TryBeginLoading(loadingKey))
                {
                    AssetHandle joinHandle = GetHandleSync(location, assetType, packageName);
                    if (joinHandle == null || joinHandle.AssetObject == null ||
                        joinHandle.Status == EOperationStatus.Failed)
                    {
                        DisposeHandle(joinHandle);
                        return null;
                    }

                    Kernel.GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, joinHandle.AssetObject, joinHandle);
                    return Kernel.TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out cachedAsset)
                        ? cachedAsset
                        : null;
                }

                int loadGeneration = unchecked((int)Kernel.UnloadGeneration);
                AssetHandle handle = null;
                try
                {
                    if (!Kernel.IsLoadingStateCurrent(loadGeneration))
                    {
                        Kernel.FailLoading(loadingKey, null);
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
                        Kernel.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return null;
                    }

                    UObject loadedAsset = handle.AssetObject;
                    Kernel.GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                    handle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    Kernel.CompleteLoading(loadingKey);
                    return loadedAsset;
                }
                catch (Exception ex)
                {
                    DisposeHandle(handle);
                    Kernel.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource Asset sync load threw. Location:{0} Package:{1} Type:{2}", location,
                        normalizedPackageName, assetType), ex));
                    return null;
                }
            }
        }

        private async UniTask<UObject> GetOrLoadAssetAsync(string location, Type assetType,
            EResourceAssetKind assetKind, string packageName, ulong loadingKey,
            uint priority = 0, CancellationToken cancellationToken = default)
        {
            string normalizedPackageName = Kernel.NormalizePackageName(packageName);
            assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, assetKind);
            assetType = ResourceKeyCodec.NormalizeAssetType(assetType, assetKind);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (Kernel.IsDestroying)
                {
                    return null;
                }

                if (Kernel.TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out UObject cachedAsset))
                {
                    return cachedAsset;
                }

                if (!Kernel.TryBeginLoading(loadingKey))
                {
                    if (!await Kernel.WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return null;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)Kernel.UnloadGeneration);
                AssetHandle handle = null;
                try
                {
                    if (!Kernel.IsLoadingStateCurrent(loadGeneration))
                    {
                        Kernel.FailLoading(loadingKey, null);
                        return null;
                    }

                    handle = GetHandleAsync(location, assetType, packageName: packageName, priority: priority);
                    if (handle == null)
                    {
                        Kernel.FailLoading(loadingKey, NewLoadingFailure("Asset", location, normalizedPackageName),
                            ELogLevel.Warning);
                        return null;
                    }

                    AttachLoadingAssetHandle(loadingKey, handle);
                    bool callerCancellationRequested = false;
                    if (!handle.IsDone)
                    {
                        await handle.ToUniTask(cancellationToken: cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        callerCancellationRequested = true;
                    }

                    if (!Kernel.IsLoadingStateCurrent(loadGeneration))
                    {
                        DisposeHandle(handle);
                        handle = null;
                        Kernel.FailLoading(loadingKey, null);
                        return null;
                    }

                    if (Kernel.ShouldAbortLoadingAfterCallerCancellation(loadingKey, cancellationToken, ref callerCancellationRequested))
                    {
                        DisposeHandle(handle);
                        handle = null;
                        Kernel.FailLoading(loadingKey, null);
                        return null;
                    }

                    if (!handle.IsValid || handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
                    {
                        Exception failure = NewLoadingFailure("Asset", location, normalizedPackageName, handle.Status,
                            handle.Error);
                        DisposeHandle(handle);
                        handle = null;
                        Kernel.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return null;
                    }

                    if (Kernel.IsDestroying)
                    {
                        DisposeHandle(handle);
                        handle = null;
                        Kernel.FailLoading(loadingKey, null);
                        return null;
                    }

                    Kernel.GetOrCreateAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                    handle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    Kernel.CompleteLoading(loadingKey);
                    if (callerCancellationRequested)
                    {
                        return null;
                    }

                    return Kernel.TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                            EResourceHandleKind.AssetHandle, out _, out cachedAsset)
                        ? cachedAsset
                        : null;
                }
                catch (OperationCanceledException)
                {
                    // 取消按契约吞成 null；去重槽必须闭环失败，否则同资源后续并发加载会在 WaitForLoadingAsync 里空转。
                    DisposeHandle(handle);
                    Kernel.FailLoading(loadingKey, null);
                    return null;
                }
                catch (Exception ex)
                {
                    DisposeHandle(handle);
                    Kernel.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource Asset load threw. Location:{0} Package:{1} Type:{2}", location, normalizedPackageName,
                        assetType), ex));
                    return null;
                }
            }
        }

        internal override async UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location,
            string packageName, EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                return ResourceLeaseHandle.Invalid;
            }

            string normalizedPackageName = Kernel.NormalizePackageName(packageName);
            ulong loadingKey = Kernel.GetLoadingOperationKey(location, normalizedPackageName, typeof(Sprite),
                EResourceAssetKind.SubAssets);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested || Kernel.IsDestroying)
                {
                    return ResourceLeaseHandle.Invalid;
                }

                if (Kernel.TryGetCachedSubAssetsRecord(normalizedPackageName, location, out int cachedAssetId))
                {
                    return Kernel.AcquireLease(cachedAssetId, EResourceLeaseKind.Binding, options);
                }

                if (!Kernel.TryBeginLoading(loadingKey))
                {
                    // 同一图集并发绑定：并入赢家的加载，不再各自发起一次 SubAssets 请求
                    if (!await Kernel.WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return ResourceLeaseHandle.Invalid;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)Kernel.UnloadGeneration);
                SubAssetsHandle subHandle = null;
                try
                {
                    if (!Kernel.IsLoadingStateCurrent(loadGeneration))
                    {
                        Kernel.FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    subHandle = GetSubAssetsHandleAsync(location, normalizedPackageName);
                    if (subHandle == null)
                    {
                        Kernel.FailLoading(loadingKey, NewLoadingFailure("SubAssets", location, normalizedPackageName),
                            ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

                    AttachLoadingSubAssetsHandle(loadingKey, subHandle);
                    bool callerCancellationRequested = false;
                    if (!subHandle.IsDone)
                    {
                        await subHandle.ToUniTask(cancellationToken: cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        callerCancellationRequested = true;
                    }

                    if (!Kernel.IsLoadingStateCurrent(loadGeneration))
                    {
                        // 强卸载/关停已发生：句柄原样交回，绝不写进已 Dispose 的 Package
                        DisposeHandle(subHandle);
                        subHandle = null;
                        Kernel.FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    bool abortedByCallerCancellation = Kernel.ShouldAbortLoadingAfterCallerCancellation(loadingKey,
                        cancellationToken, ref callerCancellationRequested);
                    bool loadFailed = !subHandle.IsValid || subHandle.Status == EOperationStatus.Failed;
                    if (abortedByCallerCancellation || loadFailed)
                    {
                        Exception failure = !abortedByCallerCancellation && loadFailed
                            ? NewLoadingFailure("SubAssets", location, normalizedPackageName, subHandle.Status,
                                subHandle.Error)
                            : null;
                        DisposeHandle(subHandle);
                        subHandle = null;
                        Kernel.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

                    int assetId = Kernel.GetOrCreateSubAssetsRecord(normalizedPackageName, location, subHandle);
                    subHandle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    Kernel.CompleteLoading(loadingKey);
                    return callerCancellationRequested
                        ? ResourceLeaseHandle.Invalid
                        : Kernel.AcquireLease(assetId, EResourceLeaseKind.Binding, options);
                }
                catch (OperationCanceledException)
                {
                    // 取消是本 API 的正常出口（契约返回 Invalid，不抛出），但已预留的去重槽必须闭环失败，
                    // 否则同图集后续并发绑定会在 WaitForLoadingAsync 里空转到各自超时/关停。
                    DisposeHandle(subHandle);
                    Kernel.FailLoading(loadingKey, null);
                    return ResourceLeaseHandle.Invalid;
                }
                catch (Exception ex)
                {
                    DisposeHandle(subHandle);
                    Kernel.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource SubAssets load threw. Location:{0} Package:{1}", location, normalizedPackageName), ex));
                    return ResourceLeaseHandle.Invalid;
                }
            }
        }

        private void AttachLoadingAssetHandle(ulong assetObjectKey, AssetHandle handle)
        {
            if (Kernel.TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                loadingOperation.AssetHandle = handle;
            }
        }

        private void AttachLoadingSubAssetsHandle(ulong assetObjectKey, SubAssetsHandle handle)
        {
            if (Kernel.TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                loadingOperation.SubAssetsHandle = handle;
            }
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

        private SubAssetsHandle GetSubAssetsHandleAsync(string location, string packageName)
        {
            return GetPackageOrThrow(packageName).LoadSubAssetsAsync<Sprite>(location);
        }

        #endregion
    }
}
