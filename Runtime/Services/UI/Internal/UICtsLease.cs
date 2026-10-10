using System.Threading;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 模块的取消源租约：装载与等待的 <see cref="CancellationTokenSource"/> 经 <see cref="MemoryPool"/> 按需取还，取消过的源还池即废。
    /// </summary>
    /// <remarks>
    /// 池只复用<b>未被取消</b>的源——取消是不可逆终态，取消过的源在 <see cref="Clear"/> 里 Dispose，下一租重建。 <br />
    /// 挂了 <c>CancelAfter</c> 超时计时的源在还池前必须自行解除计时（<c>CancelAfter(Timeout.InfiniteTimeSpan)</c>）：
    /// 活着的计时器会在池里把别的租户打取消。线程契约：仅主线程（取还与装载、等待同线程）。
    /// </remarks>
    internal sealed class UICtsLease : MemoryObject
    {
        private CancellationTokenSource _cts;

        /// <summary>本租约携带的取消源：首租或上一租的源被取消废弃后现场重建。</summary>
        internal CancellationTokenSource Source => _cts ??= new CancellationTokenSource();

        /// <inheritdoc />
        public override void Clear()
        {
            if (_cts != null && _cts.IsCancellationRequested)
            {
                _cts.Dispose();
                _cts = null;
            }
        }
    }
}
