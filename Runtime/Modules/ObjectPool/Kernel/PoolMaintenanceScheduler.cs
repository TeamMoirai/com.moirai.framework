using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池维护项接口——由需要按期维护的池实现，交由 <see cref="PoolMaintenanceScheduler"/> 调度。
    /// <para>时钟语义由所属服务决定（通用池用实时时钟、GameObject 池用缩放时钟），<paramref name="now"/> 仅透传。</para>
    /// </summary>
    internal interface IPoolMaintenanceItem
    {
        /// <summary>
        /// 执行一次维护操作（裁剪/过期释放/预算释放）。
        /// </summary>
        /// <param name="now">当前调度时钟。</param>
        /// <param name="lowMemory">是否为低内存强制维护（全量收缩）。</param>
        void ExecuteMaintenance(float now, bool lowMemory);

        /// <summary>
        /// 维护堆索引——由调度器独占维护，池方只读。
        /// </summary>
        int MaintenanceHeapIndex { get; set; }
    }

    /// <summary>
    /// 共享池维护调度器：最小堆到期唤醒 + 帧预算防卡顿。
    /// <para>仅负责"按到期时间唤醒"——到期项回调 <see cref="IPoolMaintenanceItem.ExecuteMaintenance"/>（非低内存）；
    /// 低内存全量维护由服务方自行遍历池执行，不经此调度器。</para>
    /// <para>项在执行中可重新调度自身（含到期时间早于当前时刻的"立即再醒"），帧预算保证单帧最坏 1ms。</para>
    /// </summary>
    internal sealed class PoolMaintenanceScheduler
    {
        #region 常量 [CONSTANTS]

        // 每帧用于到期维护的最大时间预算（秒），超出则剩余到期项延迟到下一帧。
        private const float FRAME_BUDGET_SECONDS = 0.001f;

        // 单次 ProcessDue 的最大执行数——独立于时间预算的确定性终止上界，
        // 防御 EditMode/时钟冻结环境下项反复以 due==now 立即重排导致的长自旋。
        private const int MAX_EXECUTIONS_PER_TICK = 1024;

        private const int INITIAL_HEAP_CAPACITY = 8;

        #endregion

        #region 结构体 [STRUCTS]

        private struct MaintenanceNode
        {
            public float DueTime;
            public IPoolMaintenanceItem Item;
        }

        #endregion

        #region 字段 [FIELDS]

        private MaintenanceNode[] _heap = new MaintenanceNode[INITIAL_HEAP_CAPACITY];
        private int _count;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取当前待维护项数量。
        /// </summary>
        public int Count => _count;

        #endregion

        #region 调度 [SCHEDULING]

        /// <summary>
        /// 调度或更新一个维护项的到期时间。
        /// </summary>
        /// <param name="item">维护项。</param>
        /// <param name="dueTime">到期时间；不小于 <see cref="float.MaxValue"/> 视为取消调度。</param>
        public void Schedule(IPoolMaintenanceItem item, float dueTime)
        {
            if (dueTime >= float.MaxValue)
            {
                Remove(item);
                return;
            }

            int heapIndex = item.MaintenanceHeapIndex;
            if (heapIndex >= 0 && heapIndex < _count && ReferenceEquals(_heap[heapIndex].Item, item))
            {
                _heap[heapIndex].DueTime = dueTime;
                SiftUp(heapIndex);
                SiftDown(heapIndex);
                return;
            }

            EnsureCapacity(_count + 1);
            int insertIndex = _count++;
            _heap[insertIndex].DueTime = dueTime;
            _heap[insertIndex].Item = item;
            item.MaintenanceHeapIndex = insertIndex;
            SiftUp(insertIndex);
        }

        /// <summary>
        /// 移除一个维护项的调度（未调度时安全）。
        /// </summary>
        /// <param name="item">维护项。</param>
        public void Remove(IPoolMaintenanceItem item)
        {
            int heapIndex = item.MaintenanceHeapIndex;
            if (heapIndex < 0 || heapIndex >= _count || !ReferenceEquals(_heap[heapIndex].Item, item))
            {
                item.MaintenanceHeapIndex = -1;
                return;
            }

            RemoveAt(heapIndex);
        }

        /// <summary>
        /// 处理所有到期项（帧预算内），逐项回调 <see cref="IPoolMaintenanceItem.ExecuteMaintenance"/>。
        /// </summary>
        /// <param name="now">当前调度时钟。</param>
        public void ProcessDue(float now)
        {
            if (_count == 0)
            {
                return;
            }

            float frameStart = Time.realtimeSinceStartup;
            int executed = 0;
            while (_count > 0 && executed < MAX_EXECUTIONS_PER_TICK)
            {
                if (_heap[0].DueTime > now)
                {
                    return;
                }

                if (Time.realtimeSinceStartup - frameStart >= FRAME_BUDGET_SECONDS)
                {
                    return;
                }

                IPoolMaintenanceItem item = _heap[0].Item;
                RemoveAt(0);
                executed++;
                item.ExecuteMaintenance(now, false);
            }
        }

        /// <summary>
        /// 清空全部调度节点（池关闭时使用；各节点索引复位为 -1）。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _count; i++)
            {
                _heap[i].Item.MaintenanceHeapIndex = -1;
                _heap[i].Item = null;
            }

            _count = 0;
        }

        #endregion

        #region 私有方法 — 堆操作 [PRIVATE HEAP OPERATIONS]

        private void RemoveAt(int index)
        {
            IPoolMaintenanceItem removed = _heap[index].Item;
            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                _heap[index] = _heap[lastIndex];
                _heap[index].Item.MaintenanceHeapIndex = index;
            }

            _heap[lastIndex] = default;
            _count = lastIndex;
            removed.MaintenanceHeapIndex = -1;
            if (index < _count)
            {
                SiftUp(index);
                SiftDown(index);
            }
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (_heap[parent].DueTime <= _heap[index].DueTime)
                {
                    break;
                }

                Swap(parent, index);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= _count)
                {
                    return;
                }

                int right = left + 1;
                int smallest = right < _count && _heap[right].DueTime < _heap[left].DueTime
                    ? right
                    : left;
                if (_heap[index].DueTime <= _heap[smallest].DueTime)
                {
                    return;
                }

                Swap(index, smallest);
                index = smallest;
            }
        }

        private void Swap(int left, int right)
        {
            (_heap[left], _heap[right]) = (_heap[right], _heap[left]);
            _heap[left].Item.MaintenanceHeapIndex = left;
            _heap[right].Item.MaintenanceHeapIndex = right;
        }

        private void EnsureCapacity(int required)
        {
            if (_heap.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _heap.Length << 1);
            Array.Resize(ref _heap, newCapacity);
        }

        #endregion
    }
}
