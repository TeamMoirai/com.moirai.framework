using System;
using System.Threading;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// Clip 租约来源：<see cref="AudioClipCache"/> 与资源后端之间的窄接缝。
    /// <para>与 <see cref="Moirai.Atropos.Audio.Middleware.IAudioMiddlewareBridge"/> 同构——缓存只认这条契约，
    /// 生产实现是 <see cref="ResourceClipLeaseSource"/>（转发到 <c>ResourceServiceHandler</c> 的租约 API），
    /// 测试可注入受控实现来断言「同地址单次加载」「引用归零才释放」这类所有权。</para>
    /// </summary>
    internal interface IAudioClipLeaseSource
    {
        /// <summary>
        /// 同步取得租约（阻塞主线程，仅限启动期/预加载）。
        /// </summary>
        /// <returns>取到有效租约返回 true。</returns>
        bool TryAcquire(string address, out AudioClipLease lease);

        /// <summary>
        /// 异步取得租约。实现方需在 <paramref name="cancellationToken"/> 取消时放弃并回调空租约，
        /// 且回调必须与调用同帧或晚于调用，不允许在调用返回前对一个已作废的地址回调。
        /// </summary>
        void AcquireAsync(string address, CancellationToken cancellationToken, Action<AudioClipLease> completed);
    }
}
