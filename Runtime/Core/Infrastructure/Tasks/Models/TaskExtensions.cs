namespace Moirai.Atropos.Tasks
{
    public static class TaskExtensions
    {
        /// <summary>
        /// 运行任务，返回 <see cref="TaskBase"/> 自身
        /// </summary>
        public static TaskBase Run(this TaskBase taskBase)
        {
            if (taskBase.GetStatus() == TaskStatus.Running)
            {
                return taskBase;
            }
            if (taskBase.HasPrerequisite()) return taskBase;
            taskBase.Start();
            TaskRunner.RegisterTask(taskBase);
            return taskBase;
        }
        
        /// <summary>
        /// 运行任务，返回 <see cref="TaskBase"/> 自身
        /// </summary>
        /// <param name="taskBase"></param>
        /// <typeparam name="TTask"></typeparam>
        /// <returns></returns>
        public static TTask Run<TTask>(this TTask taskBase) where TTask: TaskBase
        {
            return (TTask)Run((TaskBase)taskBase);
        }
    }
}
