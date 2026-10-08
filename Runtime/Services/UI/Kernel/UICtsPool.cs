using System.Collections.Generic;
using System.Threading;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 模块的取消源池：装载与等待的 <c>CancellationTokenSource</c> 按需租还，取消过的源直接废弃。
    /// </summary>
    /// <remarks>
    /// 池只回收<b>未被取消</b>的源——取消是不可逆终态，取消过的源还池即废池，当场 Dispose。 <br />
    /// 挂了 <c>CancelAfter</c> 超时计时的源在还池前必须自行解除计时（<c>CancelAfter(Timeout.InfiniteTimeSpan)</c>）： <br />
    /// 活着的计时器会在池里把别的租户打取消。线程契约：仅主线程（租还与装载、等待同线程）。
    /// </remarks>
    internal static class UICtsPool
    {
        /// <summary>池容量：装载在途与等待腿并发占用的上限量级，超出即弃。</summary>
        private const int CAPACITY = 16;

        private static readonly Stack<CancellationTokenSource> s_Pool = new Stack<CancellationTokenSource>(CAPACITY);

        /// <summary>
        /// 租一枚取消源。
        /// </summary>
        /// <returns>未触发过的取消源。</returns>
        internal static CancellationTokenSource Rent()
        {
            return s_Pool.Count > 0 ? s_Pool.Pop() : new CancellationTokenSource();
        }

        /// <summary>
        /// 还一枚取消源：取消过的当场废弃，超容量的丢弃，干净的回池。
        /// </summary>
        /// <param name="cts">待还的源；为 null 时是空操作。</param>
        internal static void Return(CancellationTokenSource cts)
        {
            if (cts == null)
            {
                return;
            }

            if (cts.IsCancellationRequested || s_Pool.Count >= CAPACITY)
            {
                cts.Dispose();
                return;
            }

            s_Pool.Push(cts);
        }
    }
}
