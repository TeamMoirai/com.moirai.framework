using System.Collections.Generic;
using Moirai.Atropos.ObjectPool;
using NUnit.Framework;

namespace ObjectPoolTests
{
    /// <summary>
    /// 共享池维护调度器回归测试：入堆/更新/移除/到期顺序/取消/清空。
    /// </summary>
    public sealed class PoolMaintenanceSchedulerTests
    {
        #region 测试桩 [TEST FAKES]

        private sealed class FakeItem : IPoolMaintenanceItem
        {
            public int MaintenanceHeapIndex { get; set; } = -1;

            public List<(float now, bool lowMemory)> Executions { get; } = new List<(float, bool)>();

            public void ExecuteMaintenance(float now, bool lowMemory)
            {
                Executions.Add((now, lowMemory));
            }
        }

        private sealed class ReschedulingItem : IPoolMaintenanceItem
        {
            private readonly PoolMaintenanceScheduler _scheduler;
            private readonly float _interval;

            public ReschedulingItem(PoolMaintenanceScheduler scheduler, float interval)
            {
                _scheduler = scheduler;
                _interval = interval;
            }

            public int MaintenanceHeapIndex { get; set; } = -1;

            public int ExecutionCount { get; private set; }

            public void ExecuteMaintenance(float now, bool lowMemory)
            {
                ExecutionCount++;
                _scheduler.Schedule(this, now + _interval);
            }
        }

        private sealed class ImmediateRescheduleItem : IPoolMaintenanceItem
        {
            private readonly PoolMaintenanceScheduler _scheduler;

            public ImmediateRescheduleItem(PoolMaintenanceScheduler scheduler)
            {
                _scheduler = scheduler;
            }

            public int MaintenanceHeapIndex { get; set; } = -1;

            public int ExecutionCount { get; private set; }

            public void ExecuteMaintenance(float now, bool lowMemory)
            {
                ExecutionCount++;
                // 模拟 "due <= now" 的立即重排（到期时间设为当前时刻）。
                _scheduler.Schedule(this, now);
            }
        }

        #endregion

        #region 调度 [SCHEDULING]

        [Test]
        public void Schedule_ThenCountIsOne()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem item = new FakeItem();

            scheduler.Schedule(item, 10f);

            Assert.AreEqual(1, scheduler.Count);
            Assert.AreEqual(0, item.MaintenanceHeapIndex);
        }

        [Test]
        public void Schedule_MaxValue_RemovesItem()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem item = new FakeItem();
            scheduler.Schedule(item, 10f);

            scheduler.Schedule(item, float.MaxValue);

            Assert.AreEqual(0, scheduler.Count);
            Assert.AreEqual(-1, item.MaintenanceHeapIndex);
        }

        [Test]
        public void Schedule_ExistingItem_UpdatesDueTimeInPlace()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem item = new FakeItem();
            scheduler.Schedule(item, 10f);

            scheduler.Schedule(item, 5f);

            Assert.AreEqual(1, scheduler.Count);
            scheduler.ProcessDue(6f);
            Assert.AreEqual(1, item.Executions.Count);
        }

        [Test]
        public void Remove_UnscheduledItem_IsSafe()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem item = new FakeItem();

            Assert.DoesNotThrow(() => scheduler.Remove(item));
            Assert.AreEqual(-1, item.MaintenanceHeapIndex);
        }

        [Test]
        public void Remove_ScheduledItem_Dequeues()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem item = new FakeItem();
            scheduler.Schedule(item, 10f);

            scheduler.Remove(item);

            Assert.AreEqual(0, scheduler.Count);
            Assert.AreEqual(-1, item.MaintenanceHeapIndex);
        }

        #endregion

        #region 到期处理 [PROCESS DUE]

        [Test]
        public void ProcessDue_EmptyScheduler_IsNoOp()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();

            Assert.DoesNotThrow(() => scheduler.ProcessDue(100f));
        }

        [Test]
        public void ProcessDue_NotYetDue_DoesNotExecute()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem item = new FakeItem();
            scheduler.Schedule(item, 10f);

            scheduler.ProcessDue(9f);

            Assert.AreEqual(0, item.Executions.Count);
            Assert.AreEqual(1, scheduler.Count);
        }

        [Test]
        public void ProcessDue_DueItem_ExecutesWithLowMemoryFalse()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem item = new FakeItem();
            scheduler.Schedule(item, 10f);

            scheduler.ProcessDue(10f);

            Assert.AreEqual(1, item.Executions.Count);
            Assert.AreEqual(10f, item.Executions[0].now);
            Assert.IsFalse(item.Executions[0].lowMemory);
        }

        [Test]
        public void ProcessDue_MultipleItems_ExecutesEachExactlyOnce()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem early = new FakeItem();
            FakeItem late = new FakeItem();
            FakeItem middle = new FakeItem();

            scheduler.Schedule(late, 30f);
            scheduler.Schedule(early, 10f);
            scheduler.Schedule(middle, 20f);

            scheduler.ProcessDue(100f);

            Assert.AreEqual(1, early.Executions.Count);
            Assert.AreEqual(1, middle.Executions.Count);
            Assert.AreEqual(1, late.Executions.Count);
            Assert.AreEqual(0, scheduler.Count);
        }

        [Test]
        public void ProcessDue_ItemReschedulesToFuture_ExecutesOncePerWake()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            ReschedulingItem item = new ReschedulingItem(scheduler, 100f);

            scheduler.Schedule(item, 10f);
            scheduler.ProcessDue(10f);

            Assert.AreEqual(1, item.ExecutionCount);
            Assert.AreEqual(1, scheduler.Count);

            scheduler.ProcessDue(20f);

            Assert.AreEqual(1, item.ExecutionCount, "not yet re-due → no extra execution");
        }

        [Test]
        public void ProcessDue_ItemReschedulesImmediately_TerminatesBounded()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            ImmediateRescheduleItem item = new ImmediateRescheduleItem(scheduler);

            scheduler.Schedule(item, 0f);
            scheduler.ProcessDue(10f);

            // 立即重排（due == now）必须被迭代上界截断（时钟冻结环境亦安全），不得无限循环。
            Assert.GreaterOrEqual(item.ExecutionCount, 1);
            Assert.LessOrEqual(item.ExecutionCount, 1024);
            Assert.AreEqual(1, scheduler.Count, "item stays scheduled for next frame");
        }

        #endregion

        #region 清空 [CLEAR]

        [Test]
        public void Clear_ResetsItemsAndCount()
        {
            PoolMaintenanceScheduler scheduler = new PoolMaintenanceScheduler();
            FakeItem a = new FakeItem();
            FakeItem b = new FakeItem();
            scheduler.Schedule(a, 10f);
            scheduler.Schedule(b, 20f);

            scheduler.Clear();

            Assert.AreEqual(0, scheduler.Count);
            Assert.AreEqual(-1, a.MaintenanceHeapIndex);
            Assert.AreEqual(-1, b.MaintenanceHeapIndex);
        }

        #endregion
    }
}
