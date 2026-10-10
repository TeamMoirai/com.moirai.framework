#if ADDRESSABLES_INSTALLED
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 加载核心与去重——赢家/等待者路径、取消闭环与图集子精灵租约，接成记录内核的赢家路径。
    /// </summary>
    partial class AddressableHandler
    {
        #region 异步加载 [ASYNC LOAD]

        private UniTask<UObject> GetOrLoadAssetAsync(string location, Type assetType,
            EResourceAssetKind assetKind, string packageName, CancellationToken cancellationToken)
        {
            string normalizedPackageName = Store.NormalizePackageName(packageName);
            assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, assetKind);
            assetType = ResourceKeyCodec.NormalizeAssetType(assetType, assetKind);
            ulong loadingKey = Store.GetLoadingOperationKey(location, normalizedPackageName, assetType, assetKind);

            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromResult<UObject>(null);
            }

            if (Store.TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                    EResourceHandleKind.AssetHandle, out _, out UObject cachedAsset))
            {
                return UniTask.FromResult(cachedAsset);
            }

            return GetOrLoadAssetPendingAsync(location, assetType, assetKind, normalizedPackageName, loadingKey,
                cancellationToken);
        }

        private async UniTask<UObject> GetOrLoadAssetPendingAsync(string location, Type assetType,
            EResourceAssetKind assetKind, string normalizedPackageName, ulong loadingKey,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested || Store._isDestroying)
                {
                    return null;
                }

                if (Store.TryGetCachedAssetRecord(normalizedPackageName, location, assetType, assetKind,
                        EResourceHandleKind.AssetHandle, out _, out UObject cachedAsset))
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

                int loadGeneration = unchecked((int)Store._unloadGeneration);
                IAddressableHandleRef handleRef = null;
                try
                {
                    if (!Store.IsLoadingStateCurrent(loadGeneration))
                    {
                        Store.FailLoading(loadingKey, null);
                        return null;
                    }

                    var handle = Addressables.LoadAssetAsync<UObject>(location);
                    handleRef = new AddressableHandleRef<UObject>(handle);
                    if (!handle.IsValid())
                    {
                        handleRef.Release();
                        Store.FailLoading(loadingKey, NewLoadingFailure(location, normalizedPackageName),
                            ELogLevel.Warning);
                        return null;
                    }

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
                        // 强卸载/关停已发生：句柄原样放掉，绝不写进已作废的记录表
                        handleRef.Release();
                        Store.FailLoading(loadingKey, null);
                        return null;
                    }

                    bool abortedByCallerCancellation = Store.ShouldAbortLoadingAfterCallerCancellation(loadingKey,
                        cancellationToken, ref callerCancellationRequested);
                    bool loadFailed = handle.Status != AsyncOperationStatus.Succeeded;
                    if (abortedByCallerCancellation || loadFailed)
                    {
                        Exception failure = !abortedByCallerCancellation && loadFailed
                            ? NewLoadingFailure(location, normalizedPackageName, handle.OperationException)
                            : null;
                        handleRef.Release();
                        Store.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return null;
                    }

                    UObject asset = handle.Result;
                    if (asset == null)
                    {
                        handleRef.Release();
                        Store.FailLoading(loadingKey, NewLoadingFailure(location, normalizedPackageName),
                            ELogLevel.Warning);
                        return null;
                    }

                    Store.GetOrCreateAssetRecord(normalizedPackageName, location, assetType,
                        assetKind, EResourceHandleKind.AssetHandle, asset, handleRef);
                    handleRef = null; // 所有权已移交记录，异常兜底不得再释放
                    Store.CompleteLoading(loadingKey);
                    return callerCancellationRequested ? null : asset;
                }
                catch (OperationCanceledException)
                {
                    // 取消是本 API 的正常出口（契约返回 null，不抛出），但已预留的去重槽必须闭环失败，
                    // 否则同地址的后续并发取用会在 WaitForLoadingAsync 里空转到各自超时/关停。
                    handleRef?.Release();
                    Store.FailLoading(loadingKey, null);
                    return null;
                }
                catch (Exception ex)
                {
                    handleRef?.Release();
                    Store.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource Asset load threw. Location:{0} Package:{1} Type:{2}", location, normalizedPackageName,
                        assetType), ex));
                    return null;
                }
            }
        }

        #endregion

        #region 图集取用 [SUB-ASSET ACQUIRE]

        /// <summary>
        /// 按图集地址取子精灵的租约：一条地址加载 <see cref="SpriteAtlas"/>，按名取用留给记录句柄。
        /// </summary>
        /// <remarks>Addressables 没有"一个地址拿全部子资产"的公开 API（<c>LoadAllAssetsAsync</c> 只在 AssetBundle 层），故对齐 YooAsset 的形态。</remarks>
        private UniTask<ResourceLeaseHandle> AcquireSubAssetsAsync(string location, string packageName,
            EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                return UniTask.FromResult(ResourceLeaseHandle.Invalid);
            }

            string normalizedPackageName = Store.NormalizePackageName(packageName);
            // 入口打包两次、全程复用：loadingKey（去重）≠ recordKey（SubAssetsHandle 口径，handleKind 位域不同）。
            ulong loadingKey = Store.GetLoadingOperationKey(location, normalizedPackageName, typeof(Sprite),
                EResourceAssetKind.SubAssets);
            ulong recordKey = Store.GetAssetRecordKey(normalizedPackageName, location, typeof(Sprite),
                EResourceAssetKind.SubAssets, EResourceHandleKind.SubAssetsHandle);

            if (cancellationToken.IsCancellationRequested || Store._isDestroying)
            {
                return UniTask.FromResult(ResourceLeaseHandle.Invalid);
            }

            if (Store.TryGetCachedSubAssetsRecordByKey(recordKey, out int cachedAssetId))
            {
                return UniTask.FromResult(Store.AcquireLease(cachedAssetId, EResourceLeaseKind.Binding, options));
            }

            return AcquireSubAssetsPendingAsync(location, normalizedPackageName, loadingKey, recordKey, options,
                cancellationToken);
        }

        private async UniTask<ResourceLeaseHandle> AcquireSubAssetsPendingAsync(string location,
            string normalizedPackageName, ulong loadingKey, ulong recordKey, EResourceLeaseOption options,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested || Store._isDestroying)
                {
                    return ResourceLeaseHandle.Invalid;
                }

                if (Store.TryGetCachedSubAssetsRecordByKey(recordKey, out int cachedAssetId))
                {
                    return Store.AcquireLease(cachedAssetId, EResourceLeaseKind.Binding, options);
                }

                if (!Store.TryBeginLoading(loadingKey))
                {
                    // 同一图集并发绑定：并入赢家的加载，不再各自发起一次请求
                    if (!await Store.WaitForLoadingAsync(loadingKey, cancellationToken))
                    {
                        return ResourceLeaseHandle.Invalid;
                    }

                    continue;
                }

                int loadGeneration = unchecked((int)Store._unloadGeneration);
                IAddressableHandleRef handleRef = null;
                try
                {
                    if (!Store.IsLoadingStateCurrent(loadGeneration))
                    {
                        Store.FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    var handle = Addressables.LoadAssetAsync<SpriteAtlas>(location);
                    handleRef = new AddressableHandleRef<SpriteAtlas>(handle);
                    if (!handle.IsValid())
                    {
                        handleRef.Release();
                        Store.FailLoading(loadingKey, NewLoadingFailure(location, normalizedPackageName),
                            ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

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
                        handleRef.Release();
                        Store.FailLoading(loadingKey, null);
                        return ResourceLeaseHandle.Invalid;
                    }

                    bool abortedByCallerCancellation = Store.ShouldAbortLoadingAfterCallerCancellation(loadingKey,
                        cancellationToken, ref callerCancellationRequested);
                    bool loadFailed = handle.Status != AsyncOperationStatus.Succeeded;
                    if (abortedByCallerCancellation || loadFailed)
                    {
                        Exception failure = !abortedByCallerCancellation && loadFailed
                            ? NewLoadingFailure(location, normalizedPackageName, handle.OperationException)
                            : null;
                        handleRef.Release();
                        Store.FailLoading(loadingKey, failure, ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

                    if (handle.Result == null)
                    {
                        handleRef.Release();
                        Store.FailLoading(loadingKey, NewLoadingFailure(location, normalizedPackageName),
                            ELogLevel.Warning);
                        return ResourceLeaseHandle.Invalid;
                    }

                    int assetId = Store.GetOrCreateSubAssetsRecordByKey(recordKey, handleRef);
                    handleRef = null; // 所有权已移交记录，异常兜底不得再释放
                    Store.CompleteLoading(loadingKey);
                    return callerCancellationRequested
                        ? ResourceLeaseHandle.Invalid
                        : Store.AcquireLease(assetId, EResourceLeaseKind.Binding, options);
                }
                catch (OperationCanceledException)
                {
                    handleRef?.Release();
                    Store.FailLoading(loadingKey, null);
                    return ResourceLeaseHandle.Invalid;
                }
                catch (Exception ex)
                {
                    handleRef?.Release();
                    Store.FailLoading(loadingKey, new GameException(StringUtility.Format(
                        "Resource SubAssets load threw. Location:{0} Package:{1}", location, normalizedPackageName), ex));
                    return ResourceLeaseHandle.Invalid;
                }
            }
        }

        #endregion

        private static GameException NewLoadingFailure(string location, string packageName)
        {
            return new GameException(StringUtility.Format(
                "Resource Asset load failed. Location:{0} Package:{1}", location, packageName));
        }

        private static GameException NewLoadingFailure(string location, string packageName, Exception error)
        {
            return new GameException(StringUtility.Format(
                "Resource Asset load failed. Location:{0} Package:{1} Error:{2}", location, packageName,
                error != null ? error.Message : string.Empty));
        }
    }
}
#endif
