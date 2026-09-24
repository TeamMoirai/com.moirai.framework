using System;
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
            string packageName, ulong loadingKey)
        {
            string normalizedPackageName = Store.NormalizePackageName(packageName);

            while (true)
            {
                if (Store.IsDestroying)
                {
                    return null;
                }

                // AssetHandle 口径下 loadingKey 即 record key：缓存命中、接力与建记录共用同一次打包结果。
                if (Store.TryGetCachedAssetRecordByKey(loadingKey, out _, out UObject cachedAsset))
                {
                    return cachedAsset;
                }

                if (!Store.TryBeginLoading(loadingKey))
                {
                    // 接力赢家：只读同一条已落地记录，绝不再开一次后端加载。
                    // 双句柄会双计引用，且后完成的赢家会把先落地的句柄 Dispose 掉（GetOrCreateAssetRecord 的择一保留）。
                    // 同步 API 不能 await：主线程被本调用占住时异步赢家没有帧可推进，同栈重入的同步赢家则在等本帧返回——
                    // 两条路都等不起。契约：记录已落地则读同一条；仍在途则 fail-fast，调用方应改用异步 API。
                    if (Store.TryGetCachedAssetRecordByKey(loadingKey, out _, out joinedAsset))
                    {
                        return joinedAsset;
                    }

                    LogUtility.Warning(
                        "Sync load cannot join in-flight load; refuse to start a second backend load. Location:{0} Package:{1}. Use async API when loading the same key concurrently.",
                        location, normalizedPackageName);
                    return null;
                }

                int loadGeneration = unchecked((int)Store.UnloadGeneration);
                AssetHandle handle = null;
                try
                {
                    if (!Store.IsLoadingStateCurrent(loadGeneration))
                    {
                        Store.FailLoading(loadingKey, null);
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
                        Store.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return null;
                    }

                    UObject loadedAsset = handle.AssetObject;
                    Store.GetOrCreateAssetRecordByKey(loadingKey, assetKind,
                        EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                    handle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    Store.CompleteLoading(loadingKey);
                    return loadedAsset;
                }
                catch (Exception ex)
                {
                    DisposeHandle(handle);
                    Store.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource Asset sync load threw. Location:{0} Package:{1} Type:{2}", location,
                        normalizedPackageName, assetType), ex));
                    return null;
                }
            }
        }

        // 命中路径不进 async：取消/关停/缓存三条判定本来就在首个 await 之前同步跑完，
        // 但方法体一旦标 async，命中也要为"已经完成的结果"造一趟状态机。剥成同步前缀 + 在途段。
        private UniTask<UObject> GetOrLoadAssetAsync(string location, Type assetType,
            EResourceAssetKind assetKind, string packageName, ulong loadingKey,
            uint priority = 0, CancellationToken cancellationToken = default)
        {
            string normalizedPackageName = Store.NormalizePackageName(packageName);
            assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, assetKind);
            assetType = ResourceKeyCodec.NormalizeAssetType(assetType, assetKind);

            if (cancellationToken.IsCancellationRequested || Store.IsDestroying)
            {
                return UniTask.FromResult<UObject>(null);
            }

            if (Store.TryGetCachedAssetRecordByKey(loadingKey, out _, out UObject cachedAsset))
            {
                return UniTask.FromResult(cachedAsset);
            }

            return GetOrLoadAssetPendingAsync(location, assetType, assetKind, normalizedPackageName, loadingKey,
                priority, cancellationToken);
        }

        private async UniTask<UObject> GetOrLoadAssetPendingAsync(string location, Type assetType,
            EResourceAssetKind assetKind, string normalizedPackageName, ulong loadingKey,
            uint priority, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (Store.IsDestroying)
                {
                    return null;
                }

                if (Store.TryGetCachedAssetRecordByKey(loadingKey, out _, out UObject cachedAsset))
                {
                    return cachedAsset;
                }

                if (!Store.TryBeginLoading(loadingKey))
                {
                    if (!await Store.WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return null;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)Store.UnloadGeneration);
                AssetHandle handle = null;
                try
                {
                    if (!Store.IsLoadingStateCurrent(loadGeneration))
                    {
                        Store.FailLoading(loadingKey, null);
                        return null;
                    }

                    handle = GetHandleAsync(location, assetType, packageName: normalizedPackageName, priority: priority);
                    if (handle == null)
                    {
                        Store.FailLoading(loadingKey, NewLoadingFailure("Asset", location, normalizedPackageName),
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

                    if (!Store.IsLoadingStateCurrent(loadGeneration))
                    {
                        DisposeHandle(handle);
                        handle = null;
                        Store.FailLoading(loadingKey, null);
                        return null;
                    }

                    if (Store.ShouldAbortLoadingAfterCallerCancellation(loadingKey, cancellationToken, ref callerCancellationRequested))
                    {
                        DisposeHandle(handle);
                        handle = null;
                        Store.FailLoading(loadingKey, null);
                        return null;
                    }

                    if (!handle.IsValid || handle.AssetObject == null || handle.Status == EOperationStatus.Failed)
                    {
                        Exception failure = NewLoadingFailure("Asset", location, normalizedPackageName, handle.Status,
                            handle.Error);
                        DisposeHandle(handle);
                        handle = null;
                        Store.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return null;
                    }

                    if (Store.IsDestroying)
                    {
                        DisposeHandle(handle);
                        handle = null;
                        Store.FailLoading(loadingKey, null);
                        return null;
                    }

                    Store.GetOrCreateAssetRecordByKey(loadingKey, assetKind,
                        EResourceHandleKind.AssetHandle, handle.AssetObject, handle);
                    handle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    Store.CompleteLoading(loadingKey);
                    if (callerCancellationRequested)
                    {
                        return null;
                    }

                    return Store.TryGetCachedAssetRecordByKey(loadingKey, out _, out cachedAsset)
                        ? cachedAsset
                        : null;
                }
                catch (OperationCanceledException)
                {
                    // 取消按契约吞成 null；去重槽必须闭环失败，否则同资源后续并发加载会在 WaitForLoadingAsync 里空转。
                    DisposeHandle(handle);
                    Store.FailLoading(loadingKey, null);
                    return null;
                }
                catch (Exception ex)
                {
                    DisposeHandle(handle);
                    Store.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource Asset load threw. Location:{0} Package:{1} Type:{2}", location, normalizedPackageName,
                        assetType), ex));
                    return null;
                }
            }
        }

        // 图集命中时整条路径没有任何可等的东西，却仍要为已完成的结果造一趟状态机；
        // 这条入口是 SetSubSprite 绑定的热路径，所以同步前缀剥在这里。
        internal override UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location,
            string packageName, EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                return UniTask.FromResult(ResourceLeaseHandle.Invalid);
            }

            string normalizedPackageName = Store.NormalizePackageName(packageName);
            ulong loadingKey = Store.GetLoadingOperationKey(location, normalizedPackageName, typeof(Sprite),
                EResourceAssetKind.SubAssets);

            if (cancellationToken.IsCancellationRequested || Store.IsDestroying)
            {
                return UniTask.FromResult(ResourceLeaseHandle.Invalid);
            }

            if (Store.TryGetCachedSubAssetsRecord(normalizedPackageName, location, out int cachedAssetId))
            {
                return UniTask.FromResult(Store.AcquireLease(cachedAssetId, EResourceLeaseKind.Binding, options));
            }

            return AcquireSubAssetsPendingAsync(location, normalizedPackageName, loadingKey, options,
                cancellationToken);
        }

        private async UniTask<ResourceLeaseHandle> AcquireSubAssetsPendingAsync(string location,
            string normalizedPackageName, ulong loadingKey, EResourceLeaseOption options,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested || Store.IsDestroying)
                {
                    return ResourceLeaseHandle.Invalid;
                }

                if (Store.TryGetCachedSubAssetsRecord(normalizedPackageName, location, out int cachedAssetId))
                {
                    return Store.AcquireLease(cachedAssetId, EResourceLeaseKind.Binding, options);
                }

                if (!Store.TryBeginLoading(loadingKey))
                {
                    // 同一图集并发绑定：并入赢家的加载，不再各自发起一次 SubAssets 请求
                    if (!await Store.WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return ResourceLeaseHandle.Invalid;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)Store.UnloadGeneration);
                SubAssetsHandle subHandle = null;
                try
                {
                    if (!Store.IsLoadingStateCurrent(loadGeneration))
                    {
                        Store.FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    subHandle = GetSubAssetsHandleAsync(location, normalizedPackageName);
                    if (subHandle == null)
                    {
                        Store.FailLoading(loadingKey, NewLoadingFailure("SubAssets", location, normalizedPackageName),
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

                    if (!Store.IsLoadingStateCurrent(loadGeneration))
                    {
                        // 强卸载/关停已发生：句柄原样交回，绝不写进已 Dispose 的 Package
                        DisposeHandle(subHandle);
                        subHandle = null;
                        Store.FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    bool abortedByCallerCancellation = Store.ShouldAbortLoadingAfterCallerCancellation(loadingKey,
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
                        Store.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

                    int assetId = Store.GetOrCreateSubAssetsRecord(normalizedPackageName, location, subHandle);
                    subHandle = null; // 所有权已移交记录，异常兜底不得再 dispose
                    Store.CompleteLoading(loadingKey);
                    return callerCancellationRequested
                        ? ResourceLeaseHandle.Invalid
                        : Store.AcquireLease(assetId, EResourceLeaseKind.Binding, options);
                }
                catch (OperationCanceledException)
                {
                    // 取消是本 API 的正常出口（契约返回 Invalid，不抛出），但已预留的去重槽必须闭环失败，
                    // 否则同图集后续并发绑定会在 WaitForLoadingAsync 里空转到各自超时/关停。
                    DisposeHandle(subHandle);
                    Store.FailLoading(loadingKey, null);
                    return ResourceLeaseHandle.Invalid;
                }
                catch (Exception ex)
                {
                    DisposeHandle(subHandle);
                    Store.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource SubAssets load threw. Location:{0} Package:{1}", location, normalizedPackageName), ex));
                    return ResourceLeaseHandle.Invalid;
                }
            }
        }

        private void AttachLoadingAssetHandle(ulong assetObjectKey, AssetHandle handle)
        {
            if (Store.TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
            {
                loadingOperation.AssetHandle = handle;
            }
        }

        private void AttachLoadingSubAssetsHandle(ulong assetObjectKey, SubAssetsHandle handle)
        {
            if (Store.TryGetLoadingOperation(assetObjectKey, out LoadingOperationState loadingOperation))
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
