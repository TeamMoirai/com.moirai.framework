using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// <see cref="IAudioClipLeaseSource"/> 的生产实现：把 clip 租约请求转发到资源后端的 Lease API。
    /// <para>缓存本身不认识 <see cref="ResourceAssetLease{T}"/>，后端换实现不影响缓存语义。</para>
    /// </summary>
    internal sealed class ResourceClipLeaseSource : IAudioClipLeaseSource
    {
        /// <inheritdoc />
        public bool TryAcquire(string address, out AudioClipLease lease)
        {
            lease = default;
            var handler = ResourceService.Handler;
            if (handler == null) return false;

            var handle = handler.LoadLease<AudioClip>(address);
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
            var handler = ResourceService.Handler;
            if (handler == null)
            {
                completed(default);
                return;
            }

            ResourceAssetLease<AudioClip> handle;
            try
            {
                handle = await handler.LoadLeaseAsync<AudioClip>(address, cancellationToken);
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
    }
}
