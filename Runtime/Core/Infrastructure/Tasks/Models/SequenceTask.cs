using System;
using System.Collections;
using System.Collections.Generic;

namespace Moirai.Atropos.Tasks
{
    /// <summary>
    /// 按顺序执行复合任务
    /// </summary>
    public class SequenceTask : PooledTaskBase<SequenceTask>, IEnumerable<TaskBase>
    {
        private readonly Queue<TaskBase> _tasks = new Queue<TaskBase>();
        
        private TaskBase _runningTask;
        
        public static SequenceTask GetPooled(TaskBase firstTask)
        {
            var task = GetPooled();
            task.Append(firstTask);
            return task;
        }
        
        public static SequenceTask GetPooled(IReadOnlyList<TaskBase> sequence)
        {
            var task = GetPooled();
            foreach (var tb in sequence)
                task.Append(tb);
            return task;
        }
        
        protected override void Reset()
        {
            base.Reset();
            // 队列里每只子任务都欠着 Append 那次 Acquire 的引用，Clear 会把它们连引用一起丢掉
            // （子任务永世回不了池；DelayTask 更是连 Timer 句柄都不取消，回调回头会把 Completed
            // 写在一只已复用的实例上）。_runningTask 仍留在队列里，摘干队列即已覆盖它。
            _runningTask = null;
            while (_tasks.Count > 0)
            {
                _tasks.Dequeue().Dispose();
            }
            Status = TaskStatus.Stopped;
        }
        
        /// <summary>
        /// 将任务附加到序列末尾
        /// </summary>
        /// <param name="task"></param>
        public SequenceTask Append(TaskBase task)
        {
            _tasks.Enqueue(task);
            task.Acquire();
            return this;
        }
        
        public SequenceTask AppendRange(IEnumerable<TaskBase> enumerable)
        {
            foreach (var task in enumerable)
                Append(task);
            return this;
        }
        
        public override void Tick()
        {
            while (true)
            {
                if (_runningTask == null)
                {
                    // 空队列（含"被清空后仍在跑"）按完成收口：旧写法在 TryPeek 失败后直接 Start() 空引用
                    if (!_tasks.TryPeek(out _runningTask))
                    {
                        CompleteTask();
                        break;
                    }

                    _runningTask.Start();
                }

                _runningTask.Tick();
                var status = _runningTask.GetStatus();
                if (status is TaskStatus.Completed or TaskStatus.Stopped)
                {
                    if (status == TaskStatus.Completed)
                    {
                        _runningTask.PostComplete();
                    }

                    _tasks.Dequeue().Dispose();
                    _runningTask = null;

                    if (_tasks.Count == 0)
                    {
                        CompleteTask();
                        break;
                    }

                    continue;
                }

                break;
            }
        }
        
        public IEnumerator<TaskBase> GetEnumerator()
        {
            return _tasks.GetEnumerator();
        }
        
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _tasks.GetEnumerator();
        }
    }
}
