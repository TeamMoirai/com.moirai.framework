using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Asset;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// <see cref="IAudioClipLeaseSource"/> 的生产实现：把 clip 租约请求转发到资源后端的 Lease API。
    /// </summary>
    /// <remarks>
    /// 缓存本身不认识 <see cref="AssetLease{T}"/>，后端换实现不影响缓存语义。 <br />
    /// 资产契约实例经容器解析一次并缓存（不调静态门面）；未就绪时维持静默降级语义。
    /// </remarks>
    internal sealed class AssetClipLeaseSource : IAudioClipLeaseSource
    {
        /// <summary>资产契约实例：首次取用时经容器解析一次并缓存。</summary>
        private IAssetService _assetService;

        /// <inheritdoc />
        public bool TryAcquire(string address, out AudioClipLease lease)
        {
            lease = default;
            if (!EnsureAssetService()) return false;

            var handle = _assetService.LoadLease<AudioClip>(address);
            if (handle.Asset == null)
            {
                handle.Dispose();
                return false;
            }

            lease = new AudioClipLease(handle.Asset, handle);
            return true;
        }

        /// <inheritdoc />
        public void AcquireAsync(string address, CancellationToken cancellationToken, Action<AudioClipLease> completed)
        {
            AcquireAsyncInternal(address, cancellationToken, completed).Forget();
        }

        private async UniTaskVoid AcquireAsyncInternal(string address, CancellationToken cancellationToken,
            Action<AudioClipLease> completed)
        {
            if (!EnsureAssetService())
            {
                completed(default);
                return;
            }

            AssetLease<AudioClip> handle;
            try
            {
                handle = await _assetService.LoadLeaseAsync<AudioClip>(address, cancellationToken);
            }
            catch (Exception e)
            {
                // 取消与失败都按「空租约」回调，由缓存的世代校验决定要不要惊动等待者
                if (!cancellationToken.IsCancellationRequested)
                {
                    LogUtility.Error("[AudioClipCache] Async lease of '{0}' failed: {1}", address, e.Message);
                }

                completed(default);
                return;
            }

            if (cancellationToken.IsCancellationRequested || handle.Asset == null)
            {
                handle.Dispose();
                completed(default);
                return;
            }

            completed(new AudioClipLease(handle.Asset, handle));
        }

        /// <summary>解析资产契约：已缓存直接命中；未就绪返回假（调用方按既有静默降级语义处置）。</summary>
        private bool EnsureAssetService()
        {
            return _assetService != null || GameServices.TryGetService(out _assetService);
        }
    }
}
