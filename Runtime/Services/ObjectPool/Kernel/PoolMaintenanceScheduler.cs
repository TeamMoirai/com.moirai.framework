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
    /// <para>每次调用分两段：采集段一次性弹出堆内全部到期项进工作集，派发段按帧预算与迭代上界逐项执行。
    /// 因此<b>本轮只处理进入本轮时已到期的项</b>——项在执行中重新调度自身（含 due &lt;= now 的"立即再醒"）
    /// 一律顺延到下一次调用，单帧每池至多维护一次。</para>
    /// <para>派发未跑完工作集（预算或上界耗尽）时，剩余项留在工作集里由后续调用续派，FIFO 不饿死；
    /// 续派前被 <see cref="Remove"/> 或 <see cref="Clear"/> 摘除的项不会再去执行。</para>
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

        // 本轮工作集：采集段写入、派发段消费。_pendingIndex 之前是已派发部分，
        // 一轮彻底排空后两者一并归零，故常态下无搬移成本。
        private IPoolMaintenanceItem[] _pending = new IPoolMaintenanceItem[INITIAL_HEAP_CAPACITY];
        private int _pendingCount;
        private int _pendingIndex;
        private bool _processing;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取堆内待维护项数量（不含本轮工作集中尚未派发的残留项，见 <see cref="PendingCount"/>）。
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// 获取本轮工作集中尚未派发的残留项数量（预算或迭代上界耗尽时跨调用续派）。
        /// </summary>
        public int PendingCount => _pendingCount - _pendingIndex;

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
                // 索引为 -1 有两种可能：确实未调度，或已被本轮采集进工作集（采集即出堆、索引同被复位）。
                // 因此工作集的摘除必须无条件执行，不能只挂在堆移除之后。
                item.MaintenanceHeapIndex = -1;
                PurgePending(item);
                return;
            }

            RemoveAt(heapIndex);
            PurgePending(item);
        }

        /// <summary>
        /// 处理到期项：先采集本轮工作集，再在帧预算与迭代上界内逐项派发；残留项留待下一次调用续派。
        /// </summary>
        /// <param name="now">当前调度时钟。</param>
        public void ProcessDue(float now)
        {
            if (_processing)
            {
                // 重入：维护回调里再驱动调度器时，工作集与游标只允许外层推进，
                // 否则会并发改写同一份缓冲（表现为漏派或同项本轮二次执行）。
                return;
            }

            if (_count == 0 && PendingCount == 0)
            {
                return;
            }

            _processing = true;
            try
            {
                CollectDue(now);

                float frameStart = Time.realtimeSinceStartup;
                int executed = 0;
                while (_pendingIndex < _pendingCount && executed < MAX_EXECUTIONS_PER_TICK)
                {
                    // 预算检查放在取项之前：残留项必须留在工作集里，不能弹出来了却不派发。
                    if (Time.realtimeSinceStartup - frameStart >= FRAME_BUDGET_SECONDS)
                    {
                        break;
                    }

                    IPoolMaintenanceItem item = _pending[_pendingIndex];
                    _pending[_pendingIndex] = null;
                    _pendingIndex++;
                    executed++;

                    try
                    {
                        item.ExecuteMaintenance(now, false);
                    }
                    catch (Exception exception)
                    {
                        // 最后一道防线：一个池的维护抛出不得截断本轮其余到期池。
                        // 采集阶段已出堆，抛出项不会被本轮重试；池若在 finally 里重排自己，
                        // 按各自退避策略于下一次调用再醒——维护是槽位泄漏的唯一回收通道，彻底摘出比热重投更糟。
                        LogUtility.Fatal(exception);
                    }
                }
            }
            finally
            {
                _processing = false;
                ResetPendingWindow();
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

            for (int i = _pendingIndex; i < _pendingCount; i++)
            {
                _pending[i] = null;
            }

            _pendingCount = 0;
            _pendingIndex = 0;
        }

        #endregion

        #region 私有方法 — 工作集 [PRIVATE PENDING WINDOW]

        /// <summary>
        /// 采集段：一次性弹出堆内全部到期项进本轮工作集（残留项保持在前面，按 FIFO 续派）。
        /// </summary>
        private void CollectDue(float now)
        {
            while (_count > 0 && _heap[0].DueTime <= now)
            {
                IPoolMaintenanceItem item = _heap[0].Item;
                RemoveAt(0);
                AppendPending(item);
            }
        }

        private void AppendPending(IPoolMaintenanceItem item)
        {
            if (_pendingCount == _pending.Length)
            {
                Array.Resize(ref _pending, _pending.Length << 1);
            }

            _pending[_pendingCount++] = item;
        }

        /// <summary>
        /// 从本轮工作集的未派发区间摘除一项（池被关闭或注销时，残留项不得再被执行）。
        /// </summary>
        private void PurgePending(IPoolMaintenanceItem item)
        {
            for (int i = _pendingIndex; i < _pendingCount; i++)
            {
                if (!ReferenceEquals(_pending[i], item))
                {
                    continue;
                }

                for (int shift = i; shift < _pendingCount - 1; shift++)
                {
                    _pending[shift] = _pending[shift + 1];
                }

                _pending[--_pendingCount] = null;
                return;
            }
        }

        /// <summary>
        /// 工作集排空后归零窗口，避免每次派发都搬移数组。
        /// </summary>
        private void ResetPendingWindow()
        {
            if (_pendingIndex < _pendingCount)
            {
                return;
            }

            for (int i = 0; i < _pendingCount; i++)
            {
                _pending[i] = null;
            }

            _pendingCount = 0;
            _pendingIndex = 0;
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
