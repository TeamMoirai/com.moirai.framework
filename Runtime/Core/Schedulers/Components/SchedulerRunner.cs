using System;
using System.Collections.Generic;
using Moirai.Atropos.Collections;
using Moirai.Atropos.Pool;
using Unity.Profiling;
using UnityEngine;

namespace Moirai.Atropos.Schedulers
{
    /// <summary>
    /// 管理更新场景中运行的所有 <see cref="IScheduled"/> 任务。
    /// 将在首次创建任务时实例化。
    /// 无需手动将其添加到场景中。类似于 Unreal 的 TimerManager。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    internal class SchedulerRunner : MonoBehaviour
    {
        /// <summary>
        /// 易于处理控制的类
        /// </summary>
        internal class ScheduledItem : IDisposable
        {
            private static readonly _ObjectPool<ScheduledItem> Pool = new _ObjectPool<ScheduledItem>(() => new ScheduledItem());
#if UNITY_EDITOR
            public double Timestamp { get; private set; }
#endif
            public IScheduled Value { get; private set; }
            
            private bool _delay;
            public TickFrame TickFrame { get; private set; }
            
            private static readonly ProfilerMarker ProfilerMarker = new ProfilerMarker("SchedulerRunner.UpdateAll.UpdateStep.UpdateItem");
         
            public static ScheduledItem GetPooled(IScheduled scheduled, TickFrame tickFrame, bool delay)
            {
                var item = Pool.Get();
                item.Value = scheduled;
#if UNITY_EDITOR
                item.Timestamp = Time.timeSinceLevelLoadAsDouble;
#endif
                item._delay = delay;
                item.TickFrame = tickFrame;
                return item;
            }
            
            /// <summary>
            /// 内部计划任务是否完成
            /// </summary>
            /// <returns></returns>
            public bool IsDone() => Value.IsDone;
            
            public void Update(TickFrame tickFrame)
            {
                using (ProfilerMarker.Auto())
                {
                    if (Value.IsDone) return;
                    if (TickFrame != tickFrame) return;
                    if (_delay)
                    {
                        _delay = false;
                        return;
                    }
                    Value.Update();
                }
            }
            
            /// <summary>
            /// 取消内部计划任务
            /// </summary>
            public void Cancel()
            {
                if (!Value.IsDone) Value.Cancel();
            }
            
            /// <summary>
            /// 释放自身和内部计划任务
            /// </summary>
            public void Dispose()
            {
                Value?.Dispose();
                Value = default;
#if UNITY_EDITOR
                Timestamp = default;
#endif
                _delay = default;
                Pool.Release(this);
            }
            
            public void Pause()
            {
                Value.Pause();
            }
            
            public void Resume()
            {
                Value.Resume();
            }
        }
        
        private const int InitialCapacity = 100;
        
        internal readonly SparseArray<ScheduledItem> ScheduledItems = new SparseArray<ScheduledItem>(InitialCapacity, SchedulerHandle.MAX_INDEX + 1);
       
        private ulong _serialNum = 1;
        
        // 缓冲区添加任务，这样就不会在迭代过程中编辑集合
        private readonly List<SchedulerHandle> _pendingHandles = new List<SchedulerHandle>(InitialCapacity);
        
        private readonly List<SchedulerHandle> _activeHandles = new List<SchedulerHandle>(InitialCapacity);
        
        private bool _isDestroyed;
        
        private bool _isGateOpen;
        
        private int _lastFrame;

        public static bool IsInitialized => (bool)_instance;
        
        private static SchedulerRunner _instance;
        
        private static readonly ProfilerMarker UpdateStepProfilerMarker =
            new ProfilerMarker("SchedulerRunner.UpdateAll.UpdateStep");
      
        public static SchedulerRunner Get()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Debug.LogError("[Scheduler] Scheduler can not be used in Editor Mode.");
                return null;
            }
#endif
            if (!_instance)
            {
                _instance = new GameObject(nameof(SchedulerRunner))
                {
                    hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable
                }.AddComponent<SchedulerRunner>();
            }
            return _instance;
        }
        
        private void Update()
        {
            UpdateAll(TickFrame.Update);
        }
        
        private void FixedUpdate()
        {
            UpdateAll(TickFrame.FixedUpdate);
        }

        private void LateUpdate()
        {
            UpdateAll(TickFrame.LateUpdate);
            _lastFrame = Time.frameCount;
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            foreach (ScheduledItem scheduledItem in ScheduledItems)
            {
                scheduledItem.Cancel();
                scheduledItem.Dispose();
            }
            SchedulerRegistry.CleanListeners();
            ScheduledItems.Clear();
            _pendingHandles.Clear();
        }
        /// <summary>
        /// 将计划任务注册到托管任务
        /// </summary>
        /// <param name="scheduled"></param>
        /// <param name="tickFrame"></param>
        /// <param name="delegate"></param>
        public void Register(IScheduled scheduled, TickFrame tickFrame, Delegate @delegate)
        {
            if (_isDestroyed)
            {
                Debug.LogWarning("[Scheduler] Can not schedule task when scene is destroying.");
                scheduled.Dispose();
                return;
            }
            // 如果在 Runner Update 之前注册，则安排下一帧
            bool needDelayFrame = _lastFrame < Time.frameCount;
            int index = scheduled.Handle.GetIndex();
            var item = ScheduledItem.GetPooled(scheduled, tickFrame, needDelayFrame);
            // 分配项目
            ScheduledItems[index] = item;
            _pendingHandles.Add(scheduled.Handle);
#if UNITY_EDITOR && !SCHEDULER_STACK_TRACE_DISABLE
            SchedulerRegistry.RegisterListener(scheduled, @delegate);
#endif
        }
        
        public SchedulerHandle NewHandle()
        {
            // 分配位置，而不是真正添加
            return new SchedulerHandle(_serialNum, ScheduledItems.AddUninitialized());
        }
        
        /// <summary>
        /// 从 managed 中取消注册计划任务
        /// </summary>
        /// <param name="scheduled"></param>
        /// <param name="delegate"></param>
        public void Unregister(IScheduled scheduled, Delegate @delegate)
        {
            ScheduledItems.RemoveAt(scheduled.Handle.GetIndex());
#if UNITY_EDITOR && !SCHEDULER_STACK_TRACE_DISABLE
            SchedulerRegistry.UnregisterListener(scheduled, @delegate);
#endif
        }
        
        /// <summary>
        /// 取消所有计划任务
        /// </summary>
        public void CancelAll()
        {
            foreach (var handle in _activeHandles)
            {
                var item = FindItem(handle);
                item.Cancel();
                if (_isGateOpen)
                {
                    item.Dispose();
                }
            }
            if (_isGateOpen)
            {
                _activeHandles.Clear();
            }
            _pendingHandles.Clear();
        }
        
        /// <summary>
        /// 暂停所有计划任务
        /// </summary>
        public void PauseAll()
        {
            foreach (var handle in _pendingHandles)
            {
                var item = FindItem(handle);
                item.Pause();
            }
            foreach (var handle in _activeHandles)
            {
                var item = FindItem(handle);
                item.Pause();
            }
        }
        
        /// <summary>
        /// 恢复所有计划任务
        /// </summary>
        public void ResumeAll()
        {
            foreach (var handle in _pendingHandles)
            {
                var item = FindItem(handle);
                item.Resume();
            }
            foreach (var handle in _activeHandles)
            {
                var item = FindItem(handle);
                item.Resume();
            }
        }
        
        private void UpdateAll(TickFrame tickFrame)
        {
            _isGateOpen = false;
            // Add
            if (_pendingHandles.Count > 0)
            {
                _activeHandles.AddRange(_pendingHandles);
                _pendingHandles.Clear();
                // increase serial
                _serialNum++;
            }

            // Update
            using (UpdateStepProfilerMarker.Auto())
            {
                for (int i = _activeHandles.Count - 1; i >= 0; --i)
                {
                    var item = FindItem(_activeHandles[i]);
                    item.Update(tickFrame);
                    if (item.IsDone())
                    {
                        _activeHandles.RemoveAt(i);
                        item.Dispose();
                    }
                }
            }

            // Shrink
            if (tickFrame == TickFrame.LateUpdate)
            {
                // 选中 Match Shrink Threshold（匹配收缩阈值），即容量大于初始容量
                bool canShrink = ScheduledItems.InternalCapacity > 2 * InitialCapacity;
                // 当未分配的元素远大于已分配的元素时收缩列表
                bool overlapAllocated = ScheduledItems.NumFreeIndices > 4 * ScheduledItems.Count;
                if (canShrink && overlapAllocated)
                {
                    ScheduledItems.Shrink();
                }
            }
            _isGateOpen = true;
        }
        
        private ScheduledItem FindItem(SchedulerHandle handle)
        {
            int handleIndex = handle.GetIndex();
            ulong handleSerial = handle.GetSerialNumber();
            var scheduledItem = ScheduledItems[handleIndex];
            if (scheduledItem == null || scheduledItem.Value.Handle.GetSerialNumber() != handleSerial) return null;
            return scheduledItem;
        }
        
        /// <summary>
        /// 内部计划任务是否完成
        /// </summary>
        /// <param name="handle"></param>
        /// <returns></returns>
        public bool IsDone(SchedulerHandle handle)
        {
            var item = FindItem(handle);
            return item == null || item.IsDone();
        }
        
        /// <summary>
        /// 取消目标计划任务
        /// </summary>
        /// <param name="handle"></param>
        public void Cancel(SchedulerHandle handle)
        {
            var item = FindItem(handle);
            if (item == null) return;
            item.Cancel();
            // 确保 pending buffer 也删除任务
            if (_pendingHandles.Remove(handle))
            {
                item.Dispose();
            }
            else if (_isGateOpen)
            {
                _activeHandles.Remove(handle);
                item.Dispose();
            }
        }
        
        /// <summary>
        /// 暂停目标计划任务
        /// </summary>
        /// <param name="handle"></param>
        public void Pause(SchedulerHandle handle)
        {
            var item = FindItem(handle);
            item?.Pause();
        }
        
        /// <summary>
        /// 恢复目标计划任务
        /// </summary>
        /// <param name="handle"></param>
        public void Resume(SchedulerHandle handle)
        {
            var item = FindItem(handle);
            item?.Resume();
        }
    }
}