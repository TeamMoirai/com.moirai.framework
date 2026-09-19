using Moirai.Atropos.Timer;

namespace Moirai.Atropos.Tasks
{
    /// <summary>
    /// 延迟任务（TimerService 驱动，可跟踪句柄）。
    /// </summary>
    public class DelayTask : PooledTaskBase<DelayTask>
    {
        private ulong _handle;

        [StackTraceFrame]
        public static unsafe DelayTask GetPooled(float delay)
        {
            var task = GetPooled();
            task._handle = TimerService.DelayUnsafe(delay, new TimerUnsafeBinding(task, &StopDelayTask));
            return task;
        }

        protected override void Init()
        {
            base.Init();
            _handle = 0UL;
        }

        protected override void Reset()
        {
            base.Reset();
            _handle.Cancel();
            _handle = 0UL;
        }

        private static void StopDelayTask(object instance)
        {
            ((DelayTask)instance).Status = TaskStatus.Completed;
        }
    }
}
