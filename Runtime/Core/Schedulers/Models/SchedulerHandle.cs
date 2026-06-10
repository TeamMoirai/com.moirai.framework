using System;
using UnityEngine.Assertions;

namespace Moirai.Atropos.Schedulers
{
    /// <summary>
    /// Handle 允许访问跟踪计划任务
    /// </summary>
    public readonly struct SchedulerHandle : IDisposable
    {
        /// <summary>
        /// 计划任务的句柄 ID
        /// </summary>
        /// <value></value>
        public ulong Handle { get; }
        
        public const int INDEX_BITS = 24;
        public const int SERIAL_NUMBER_BITS = 40;
        public const int MAX_INDEX = 1 << INDEX_BITS;
        public const ulong MAX_SERIAL_NUMBER = (ulong)1 << SERIAL_NUMBER_BITS;
        
        public int GetIndex() => (int)(Handle & MAX_INDEX - 1);
        
        public ulong GetSerialNumber() => Handle >> INDEX_BITS;
        
        /// <summary>
        /// 获取计划任务是否有效
        /// </summary>
        /// <value></value>
        public bool IsValid()
        {

            if (!SchedulerRunner.IsInitialized) return default;
            return Handle != 0;
        }
        
        /// <summary>
        /// 获取计划任务是否完成
        /// </summary>
        /// <value></value>
        public bool IsDone()
        {
            return SchedulerRunner.IsInitialized ? SchedulerRunner.Get().IsDone(this) : default;
        }
        
        public SchedulerHandle(ulong handle)
        {
            Handle = handle;
        }
        
        public SchedulerHandle(ulong serialNum, int index)
        {
            Assert.IsTrue(index >= 0 && index < MAX_INDEX);
            Assert.IsTrue(serialNum < MAX_SERIAL_NUMBER);
#pragma warning disable CS0675
            Handle = (serialNum << INDEX_BITS) | (ulong)index;
#pragma warning restore CS0675
        }
        
        /// <summary>
        /// 如果计划任务有效，则取消
        /// </summary>
        /// <value></value>
        public void Cancel()
        {
            if (!SchedulerRunner.IsInitialized) return;
            if (!IsValid()) return;
            SchedulerRunner.Get().Cancel(this);
        }
        
        /// <summary>
        /// 如果计划任务有效，则暂停
        /// </summary>
        /// <value></value>
        public void Pause()
        {
            if (!SchedulerRunner.IsInitialized) return;
            if (!IsValid()) return;
            SchedulerRunner.Get().Pause(this);
        }
        
        /// <summary>
        /// 如果有效，则恢复计划任务
        /// </summary>
        /// <value></value>
        public void Resume()
        {
            if (!SchedulerRunner.IsInitialized) return;
            if (!IsValid()) return;
            SchedulerRunner.Get().Resume(this);
        }

        public void Dispose()
        {
            Cancel();
        }
        
        public static bool operator ==(SchedulerHandle left, SchedulerHandle right)
        {
            return left.Handle == right.Handle;
        }
        
        public static bool operator !=(SchedulerHandle left, SchedulerHandle right)
        {
            return left.Handle != right.Handle;
        }
        
        public override bool Equals(object obj)
        {
            if (obj is not SchedulerHandle handle) return false;
            return handle.Handle == Handle;
        }
        
        public override int GetHashCode()
        {
            return Handle.GetHashCode();
        }
    }
}