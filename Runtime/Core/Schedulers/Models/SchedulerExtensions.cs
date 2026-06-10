using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Schedulers
{
    public static class SchedulerExtensions
    {
        /// <summary>
        /// 调度程序的异步 API
        /// </summary>
        /// <param name="handle"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="disposeSchedulerWhenCancelled">在取消时也释放 scheduler</param>
        /// <returns></returns>
        public static UniTask WaitAsync(this SchedulerHandle handle, CancellationToken cancellationToken = default, bool disposeSchedulerWhenCancelled = true)
        {
            if (disposeSchedulerWhenCancelled)
                handle.AddTo(cancellationToken);
            return UniTask.WaitUntil(handle.IsDone, cancellationToken: cancellationToken);
        }
        
        internal static void Assign(this IScheduled scheduled, ref SchedulerHandle handle)
        {
            handle.Dispose();
            handle = scheduled.Handle;
        }
    }
}