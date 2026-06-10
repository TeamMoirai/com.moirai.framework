using System;

namespace Moirai.Atropos.Schedulers
{
    /// <summary>
    /// 可以安排计划的接口
    /// </summary>
    internal interface IScheduled : IDisposable
    {
        /// <summary>
        /// 获取任务的句柄
        /// </summary>
        /// <value></value>
        SchedulerHandle Handle { get; }

        /// <summary>
        /// 获取任务是否因各种原因完成运行。
        /// </summary>
        bool IsDone { get; }

        /// <summary>
        /// 任务当前是否暂停。
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 更新任务
        /// </summary>
        void Update();

        /// <summary>
        /// 停止正在进行或已暂停的任务。不会调用任务的 on completion 回调。
        /// </summary>
        void Cancel();

        /// <summary>
        /// 暂停正在运行的调度程序。暂停的任务可以从暂停的时间点恢复。
        /// </summary>
        void Pause();

        /// <summary>
        /// 继续暂停的任务。如果任务尚未暂停，则不执行任意操作。
        /// </summary>
        void Resume();
    }
}