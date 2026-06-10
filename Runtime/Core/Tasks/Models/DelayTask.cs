using Moirai.Atropos.Schedulers;

namespace Moirai.Atropos.Tasks
{
    /// <summary>
    /// 使用调度程序（<see cref="Scheduler"/>）的延迟任务，以便可以跟踪
    /// </summary>
    public class DelayTask : PooledTaskBase<DelayTask>
    {
        private SchedulerHandle _handle;
        
        [StackTraceFrame]
        public static unsafe DelayTask GetPooled(float delay)
        {
            var task = GetPooled();
            task._handle = Scheduler.DelayUnsafe(delay, new SchedulerUnsafeBinding(task, &StopDelayTask));
            return task;
        }
        
        protected override void Init()
        {
            base.Init();
            _handle = default;
        }
        
        protected override void Reset()
        {
            base.Reset();
            _handle.Dispose();
            _handle = default;
        }
        
        private static void StopDelayTask(object instance)
        {
            ((DelayTask)instance).Status = TaskStatus.Completed;
        }
    }
}
