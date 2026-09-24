#if ADDRESSABLES_INSTALLED
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Addressables 后端的取用面——记录内核的持有者，也是内核看向本后端的唯一一面。
    /// <para>记账（记录槽、租约、去重、时间轮）全在 <see cref="ResourceRecordStore"/>，与 YooAsset 后端共用同一份；
    /// 本文件只做三件事：把 Addressables 的异步加载接成内核要的"赢家路径"、把 <c>AsyncOperationHandle&lt;T&gt;</c>
    /// 包成内核能存放的引用型句柄、以及把三个配置读数交给内核。</para>
    /// <para><c>Addressables</c> 没有同步取资产的公开 API（只有 <c>WaitForAsyncOpsToWrapUp</c> 这类等全部的），
    /// 所以同步族的 <c>AcquireDirect</c> / <c>LoadLease&lt;T&gt;</c> / <c>AcquireBinding</c> /
    /// <c>AcquirePrefabSourceLease</c> 与图集族仍按 <see cref="CreateNotSupported"/> 快速失败——
    /// 这是后端能力差，不是没写完，静默返回 Invalid 才是必须避免的假成功。</para>
    /// </summary>
    internal sealed partial class AddressableHandler : IResourceRecordHost
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
        /// <remarks>图集（子资源）句柄还没接：那条路径整体走 <see cref="CreateNotSupported"/>，
        /// 所以本方法当前的唯一调用方拿不到非空结果——不是静默降级，是该入口尚未实现。</remarks>
        Sprite IResourceRecordHost.GetSubSprite(object handle, string spriteName)
        {
            return null;
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
        /// 引用型句柄包装。<see cref="AsyncOperationHandle{TObject}"/> 是 struct 且与非泛型版无共同基类，
        /// 直接塞进内核的 <c>object</c> 槽会装箱、且解箱要精确知道 T；这里用"类持 struct 字段"绕开两者。
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
        /// 取用一条资源并挂上租约——Addressables 侧唯一可行的形态是异步，
        /// 因此同步族不接（见类头说明），这里只服务异步成员。
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
