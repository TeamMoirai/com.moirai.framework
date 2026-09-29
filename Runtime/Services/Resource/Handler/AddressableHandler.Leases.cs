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
    /// Addressables 后端的取用面：记录内核的持有者，也是内核看向本后端的唯一一面。
    /// </summary>
    /// <remarks>
    /// 记账（记录槽、租约、去重、时间轮）全在 <see cref="ResourceRecordStore"/>，与 YooAsset 后端共用同一份。
    /// 本文件把异步加载接成内核的赢家路径、把 <c>AsyncOperationHandle&lt;T&gt;</c> 包成引用型句柄、并把三个配置读数交给内核。
    /// Addressables 无同步取资产 API，故同步族与图集族按 <see cref="CreateNotSupported"/> 快速失败，不静默返回 Invalid。
    /// </remarks>
    partial class AddressableHandler : IResourceRecordHost
    {
        #region 内核接线 [KERNEL WIRING]

        [NonSerialized] private ResourceRecordStore _store;

        private ResourceRecordStore Store =>
            _store ??= new ResourceRecordStore(this, () => DefaultPackageName);

        internal int LoadingOperationCount => Store.LoadingOperationCount;

        /// <inheritdoc />
        bool IResourceRecordHost.IsHandleValid(object handle)
        {
            return (handle as IAddressableHandleRef)?.IsValid == true;
        }

        /// <inheritdoc />
        void IResourceRecordHost.DisposeHandle(object handle)
        {
            (handle as IAddressableHandleRef)?.Release();
        }

        /// <inheritdoc />
        /// <remarks>只有图集形态的记录（<see cref="SpriteAtlas"/>）有子精灵可取；单资产句柄返回 null
        /// 是正解，不是降级。按名取用走 <c>SpriteAtlas.GetSprite</c>——本机 6000.3 的 CoreModule 里
        /// 并没有 <c>TryGetSprite</c>（那串只出现在 TextCore 模块的另一套类型上），别照着记忆写。</remarks>
        Sprite IResourceRecordHost.GetSubSprite(object handle, string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName) || !(handle is AddressableHandleRef<SpriteAtlas> atlasRef))
            {
                return null;
            }

            return atlasRef.GetSprite(spriteName);
        }

        // 三个配置读数不再另写一遍显式实现：容量属性本来就是 public 的 get/set，
        // 隐式即满足接口的 get 要求，多写一层只会让两处读数各说各话。

        #endregion
        
        #region 句柄包装 [HANDLE WRAPPER]

        /// <summary>内核侧只需要"还活着吗"和"放掉"，不需要知道 Addressables 的泛型参数。</summary>
        private interface IAddressableHandleRef
        {
            bool IsValid { get; }

            void Release();
        }

        /// <summary>
        /// 引用型句柄包装：以类持 struct 字段，避免句柄进内核 <c>object</c> 槽时装箱与解箱。
        /// </summary>
        private sealed class AddressableHandleRef<TObject> : IAddressableHandleRef
        {
            private AsyncOperationHandle<TObject> _handle;
            private bool _released;

            internal AddressableHandleRef(AsyncOperationHandle<TObject> handle)
            {
                _handle = handle;
            }

            /// <summary>释放标记自己扛：不依赖 <c>IsValid()</c> 在 Release 之后是否转 false。</summary>
            public bool IsValid => !_released && _handle.IsValid();

            /// <summary>
            /// 图集形态下按名取子精灵，其余 <c>TObject</c> 恒返回 <c>null</c>。
            /// </summary>
            internal Sprite GetSprite(string spriteName)
            {
                if (_released || !_handle.IsValid())
                {
                    return null;
                }

                return (_handle.Result as SpriteAtlas)?.GetSprite(spriteName);
            }

            public void Release()
            {
                if (_released)
                {
                    return;
                }

                _released = true;
                if (_handle.IsValid())
                {
                    Addressables.Release(_handle);
                }

                _handle = default;
            }
        }

        #endregion
        
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
                if (cancellationToken.IsCancellationRequested || Store.IsDestroying)
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

                int loadGeneration = unchecked((int)Store.UnloadGeneration);
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

        /// <summary>
        /// 取用一条资源并挂上租约，只服务异步成员（Addressables 侧唯一可行的形态是异步）。
        /// </summary>
        private async UniTask<ResourceLeaseHandle> AcquireLeaseAsync(ResourceKey key, EResourceLeaseKind leaseKind,
            EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            Type assetType = key.AssetType ?? typeof(UObject);
            EResourceAssetKind assetKind = ResourceKeyCodec.NormalizeAssetKind(assetType, key.AssetKind);
            UObject resource = await GetOrLoadAssetAsync(key.Location, assetType, assetKind, key.PackageName,
                cancellationToken);
            if (resource == null)
            {
                return ResourceLeaseHandle.Invalid;
            }

            string normalizedPackageName = Store.NormalizePackageName(key.PackageName);
            ulong recordKey = Store.GetAssetRecordKey(normalizedPackageName, key.Location, assetType, assetKind,
                EResourceHandleKind.AssetHandle);
            if (!Store.TryGetRecordId(recordKey, out int assetId))
            {
                return ResourceLeaseHandle.Invalid;
            }

            return Store.AcquireLease(assetId, leaseKind, options);
        }

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

            if (cancellationToken.IsCancellationRequested || Store.IsDestroying)
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
                if (cancellationToken.IsCancellationRequested || Store.IsDestroying)
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

                int loadGeneration = unchecked((int)Store.UnloadGeneration);
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

        #endregion
    }
}
#endif
