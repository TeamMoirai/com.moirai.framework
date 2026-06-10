using System;
using System.Collections.Generic;
using Moirai.Atropos.Events;
using Moirai.Atropos.Pool;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Pool;

namespace Moirai.Atropos.Tasks
{
    public enum TaskStatus
    {
        /// <summary>
        /// 任务已启用运行并且可以更新
        /// </summary>
        Running,
        
        /// <summary>
        /// 任务已暂停，将被忽略
        /// </summary>
        Paused,
        
        /// <summary>
        /// 任务已完成，等待广播完成事件
        /// </summary>
        Completed,
        
        /// <summary>
        /// 任务已停止，并且不会广播完成事件
        /// </summary>
        Stopped
    }
    
    public interface ITaskEvent { }
    
    public sealed class TaskCompleteEvent : EventBase<TaskCompleteEvent>, ITaskEvent
    {
        [JsonIgnore]
        public TaskBase Task { get; private set; }
        
        /// <summary>
        /// 对 subtask 的软引用，监听器可以在广播此事件之前被 disposition 处理。
        /// 因此们检查 subtask 的 prerequisite 是否包含此事件，以确定其生命周期版本。
        /// </summary>
        /// <returns></returns>
        [JsonIgnore]
        public readonly List<TaskBase> Listeners = new List<TaskBase>();
        
        public static TaskCompleteEvent Create(TaskBase task)
        {
            var evt = GetPooled();
            evt.Task = task;
            evt.Listeners.Clear();
            return evt;
        }
        
        protected override void Init()
        {
            base.Init();
            Propagation = EventPropagation.Bubbles | EventPropagation.TricklesDown;
        }
        
        public void AddListenerTask(TaskBase taskBase)
        {
            Listeners.Add(taskBase);
        }
        
        public void RemoveListenerTask(TaskBase taskBase)
        {
            Listeners.Remove(taskBase);
        }
    }
    
    /// <summary>
    /// 框架任务的基类
    /// </summary>
    public abstract class TaskBase : CallbackEventHandler, IDisposable
    {
        protected TaskStatus Status;

        private IEventCoordinator _coordinator;

        public override IEventCoordinator Coordinator => _coordinator;

        public override void SendEvent(EventBase e, DispatchMode dispatchMode = DispatchMode.Default)
        {
            e.Target = this;
            EventManager.Instance.Dispatch(e, dispatchMode, MonoDispatchType.Update);
        }

        public virtual TaskStatus GetStatus()
        {
            return Status;
        }
        
        public abstract string GetTaskID();
        
        /// <summary>
        /// 调试使用情况
        /// </summary>
        /// <returns></returns>
        protected virtual string GetTaskName()
        {
#if UNITY_EDITOR
            return GetType().Name;
#else
            return string.Empty;
#endif
        }
        
        internal string InternalGetTaskName() => GetTaskName();
        
        #region 生命周期 [LIFETIME CYCLE]
        
        protected virtual void Init()
        {
            _completeEvent = TaskCompleteEvent.Create(this);
            _coordinator = EventManager.Instance;
            Status = TaskStatus.Stopped;
        }
        
        public virtual void Stop()
        {
            Status = TaskStatus.Stopped;
        }
        
        public virtual void Start()
        {
            Status = TaskStatus.Running;
        }
        
        public virtual void Pause()
        {
            Status = TaskStatus.Paused;
        }

        public virtual void Tick()
        {

        }
        
        protected void CompleteTask()
        {
            Status = TaskStatus.Completed;
        }
        
        protected virtual void Reset()
        {
            Status = TaskStatus.Stopped;
        }
        
        public virtual void Acquire()
        {

        }
        
        public virtual void Dispose()
        {
            if (_prerequisites != null)
            {
                HashSetPool<TaskCompleteEvent>.Release(_prerequisites);
                _prerequisites = null;
            }
            if (_completeEvent != null)
            {
                _completeEvent.Dispose();
                _completeEvent = null;
            }
            
            Parent = null;
        }
        
        #endregion
        
        #region 先决条件管理 [PREREQUISITES MANAGEMENT]
        
        private HashSet<TaskCompleteEvent> _prerequisites;
        
        private TaskCompleteEvent _completeEvent;
        
        internal void PostComplete()
        {
            SendEvent(_completeEvent);
            HandleEventAtTargetPhase(_completeEvent);
            _completeEvent.Dispose();
            _completeEvent = null;
        }
        
        /// <summary>
        /// 发布先决条件（如果包含其引用）
        /// </summary>
        /// <param name="evt"></param>
        /// <returns></returns>
        internal bool ReleasePrerequisite(TaskCompleteEvent evt)
        {
            return _prerequisites.Remove(evt);
        }
        
        internal bool HasPrerequisite()
        {
            return _prerequisites != null && _prerequisites.Count > 0;
        }
        
        /// <summary>
        /// 获取任务完成事件
        /// </summary>
        /// <returns></returns>
        public TaskCompleteEvent GetCompleteEvent()
        {
            return _completeEvent;
        }
        
        /// <summary>
        /// 在此任务运行之前添加先决条件任务
        /// </summary>
        /// <param name="taskBase"></param>
        public void RegisterPrerequisite(TaskBase taskBase)
        {
            var evt = taskBase.GetCompleteEvent();
            evt.AddListenerTask(this);
            _prerequisites ??= HashSetPool<TaskCompleteEvent>.Get();
            _prerequisites.Add(evt);
        }
        
        /// <summary>
        /// 删除先决条件任务（如果存在）
        /// </summary>
        /// <param name="taskBase"></param>
        public bool UnregisterPrerequisite(TaskBase taskBase)
        {
            if (_prerequisites == null) return false;
            var evt = taskBase.GetCompleteEvent();
            // 不应在 dispatch 时编辑集合
            if (!evt.Dispatch)
                evt.RemoveListenerTask(this);
            return _prerequisites.Remove(evt);
        }
        
        #endregion 先决条件管理 [PREREQUISITES MANAGEMENT]
    }
    public abstract class PooledTaskBase<T> : TaskBase where T : PooledTaskBase<T>, new()
    {
        private int _refCount;
        
        private bool _pooled;
        
        private static readonly _ObjectPool<T> Pool = new _ObjectPool<T>(() => new T());
        
        private static readonly string DefaultName;
        
        static PooledTaskBase()
        {
            DefaultName = typeof(T).Name;
        }
        
        protected PooledTaskBase() : base()
        {
            _refCount = 0;
        }
        
        public sealed override void Dispose()
        {
            if (--_refCount == 0)
            {
                base.Dispose();
                ReleasePooled((T)this);
            }
        }
        
        private static void ReleasePooled(T evt)
        {
            if (evt._pooled)
            {
                evt.Reset();
                Pool.Release(evt);
                evt._pooled = false;
            }
        }
        
        public static T GetPooled()
        {
            T t = Pool.Get();
            t.Init();
            t._pooled = true;
            return t;
        }
        
        protected override void Init()
        {
            base.Init();
            if (_refCount != 0)
            {
                Debug.LogWarning($"Task improperly released, reference count {_refCount}.");
                _refCount = 0;
            }
        }
        
        public override void Acquire()
        {
            _refCount++;
        }
        
        public override string GetTaskID()
        {
            return DefaultName;
        }
    }
}
