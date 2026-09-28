using System.Collections.Generic;
using Moirai.Atropos.Events;
using UnityEngine;
#if R3_INSTALLED
using Moirai.Atropos.R3;
using R3;
#endif

namespace Moirai.Atropos.Tasks
{
    internal class TaskRunner : MonoBehaviour
    {
        // ── 任务 Tick 异常分级：开发期 Fatal 后上抛（第一时间暴露缺陷），发布期隔离续跑（一只坏任务不拖垮整帧）──
        // const 门控：JIT 裁剪死分支，Release 零运行时成本。与内核 ServiceScope / EventDispatcher /
        // PlayerLoopDriver / MemoryPoolRegistry 的同形常量语义一致；五处重复体的收口已排进重构方案 S3。
        internal const bool RETHROW_TASK_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                true;
#else
                false;
#endif

        private class TaskCallbackEventHandler: CallbackEventHandler
        {
            public override IEventCoordinator Coordinator => EventManager.Instance;

            public TaskCallbackEventHandler()
            {
                Parent = EventManager.EventHandler;
            }
            
            public override void SendEvent(EventBase e, DispatchMode dispatchMode = DispatchMode.Default)
            {
                e.Target = this;
                EventManager.Instance.Dispatch(e, dispatchMode, MonoDispatchType.Update);
            }
        }
        
        internal readonly List<TaskBase> Tasks = new List<TaskBase>();
        
        private readonly List<TaskBase> _tasksToAdd = new List<TaskBase>();
        
        private static TaskRunner s_Instance;
        
        private static TaskRunner GetInstance()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return null;
#endif
            if (s_Instance == null)
            {
                GameObject managerObject = new GameObject { name = $"[{nameof(TaskRunner)}]" };
                s_Instance = managerObject.AddComponent<TaskRunner>();
                DontDestroyOnLoad(s_Instance);
            }
            return s_Instance;
        }

        private TaskCallbackEventHandler _eventHandler;
        
        public static void RegisterTask(TaskBase task)
        {
            GetInstance()?.Internal_RegisterTask(task);
        }

        /// <summary>
        /// 登记任务（静态 <see cref="RegisterTask"/> 的全部实质）。留出 internal 入口给测试与代码装配：
        /// 静态那层还要取宿主，而宿主在编辑器态拿不到（见 <see cref="GetInstance"/>）。
        /// </summary>
        internal void Internal_RegisterTask(TaskBase task)
        {
            if (Tasks.Contains(task))
            {
                LogUtility.Warning($"[TaskRunner] Task {task.InternalGetTaskName()} has already been registered!");
                return;
            }
            task.Acquire();
            task.Parent = GetEventHandler();
            _tasksToAdd.Add(task);
        }
        
        private void Awake()
        {
#if R3_INSTALLED
            GetEventHandler().AsObservable<TaskCompleteEvent>()
                             .SubscribeSafe(OnTaskComplete)
                             .RegisterTo(destroyCancellationToken);
#endif
        }
        
        private void Update()
        {
            UpdateAllTasks();
        }

        private void OnDestroy()
        {
            ReleaseAllTasks();

            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        /// <summary>摘干两张任务表并各自 Dispose（OnDestroy 的全部实质，留出入口给测试与代码装配）。</summary>
        internal void ReleaseAllTasks()
        {
            for (int i = 0; i < _tasksToAdd.Count; i++)
            {
                _tasksToAdd[i].Dispose();
            }
            _tasksToAdd.Clear();

            for (int i = 0; i < Tasks.Count; i++)
            {
                Tasks[i].Dispose();
            }
            Tasks.Clear();
        }

        public CallbackEventHandler GetEventHandler()
        {
            return _eventHandler ??= new TaskCallbackEventHandler();
        }
        
        internal void UpdateAllTasks()
        {
            if (_tasksToAdd.Count > 0)
            {
                Tasks.AddRange(_tasksToAdd);
                _tasksToAdd.Clear();
            }

            bool rethrow = RETHROW_TASK_EXCEPTIONS;
            for (int i = 0; i < Tasks.Count; i++)
            {
                var task = Tasks[i];
                if (task.GetStatus() != TaskStatus.Running)
                {
                    continue;
                }

                try
                {
                    task.Tick();
                }
                catch (System.Exception ex)
                {
                    // 每帧轮询的异常隔离（CLAUDE.md 明文允许的例外：订阅/任务抛出不得截断同帧其余项）。
                    // 先 Stop 再报：毒任务由此落到下面的收尾循环被摘除 Dispose，
                    // 开发期上抛让缺陷当场可见，发布期隔离续跑。
                    task.Stop();
                    LogUtility.Fatal($"[TaskRunner] Task {task.InternalGetTaskName()} threw during Tick and was stopped: {ex}");
                    if (rethrow)
                    {
                        throw;
                    }
                }
            }

            for (int i = Tasks.Count - 1; i >= 0; i--)
            {
                var status = Tasks[i].GetStatus();
                if (status is TaskStatus.Completed or TaskStatus.Stopped)
                {
                    if (status == TaskStatus.Completed)
                    {
                        Tasks[i].PostComplete();
                    }
                    Tasks[i].Dispose();
                    Tasks.RemoveAt(i);
                }
            }
        }
        
        private static void OnTaskComplete(TaskCompleteEvent evt)
        {
            foreach (var task in evt.Listeners)
            {
                if (task.ReleasePrerequisite(evt) && !task.HasPrerequisite())
                {
                    task.Run();
                }
            }
        }
    }
}