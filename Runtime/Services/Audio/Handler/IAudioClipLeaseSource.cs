using System;
using System.Threading;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// Clip 租约来源：<see cref="AudioClipCache"/> 与资源后端之间的窄接缝。
    /// </summary>
    /// <remarks>缓存只认这条契约；生产实现是 <see cref="ResourceClipLeaseSource"/>（转发到 <c>ResourceServiceHandler</c> 的租约 API），测试可注入受控实现。 <br />
    /// </remarks>
    internal interface IAudioClipLeaseSource
    {
        /// <summary>
        /// 同步取得租约（阻塞主线程，仅限启动期/预加载）。
        /// </summary>
        /// <returns>取到有效租约返回 true。</returns>
        bool TryAcquire(string address, out AudioClipLease lease);

        /// <summary>异步取得租约。</summary>
        /// <remarks>取消时须放弃并回调空租约；回调必须与调用同帧或晚于调用，不允许在返回前对已作废地址回调。</remarks>
        void AcquireAsync(string address, CancellationToken cancellationToken, Action<AudioClipLease> completed);
    }
}
