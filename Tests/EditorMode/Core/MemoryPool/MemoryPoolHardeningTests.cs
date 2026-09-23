using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine.TestTools;
using Mp = Moirai.Atropos.MemoryPool;

namespace Core.MemoryPool
{
    /// <summary>
    /// 上线加固面回归：存活上限（漏还可见性）、在外高水位、结构自检、批量异常采集上限、维护边界上报。
    /// <para>这些判据都是"发布包里不会当场报错、几周后才以 OOM 或随机崩溃回来"的那一类，
    /// 所以配套的是可发现的边界与只读自检，而不是新的运行时约束。</para>
    /// </summary>
    public sealed class MemoryPoolHardeningTests : MemoryPoolFixture
    {
        [Test]
        public void LiveLimitRejectsExtraLeaseInDevelopment()
        {
            const int limit = 4;
            MemoryPool<PoolItem>.SetLiveLimit(limit);
            PoolItem[] items = new PoolItem[limit];
            for (int i = 0; i < limit; i++)
            {
                items[i] = MemoryPool<PoolItem>.Acquire();
            }

            Assert.AreEqual(limit, Info<PoolItem>().UsingCount);
            Assert.AreEqual(limit, Info<PoolItem>().LiveLimit);

            LogAssert.ignoreFailingMessages = true;
            try
            {
                // 开发期先报后抛：漏还要在第一现场被人看见，而不是等正式包 OOM。
                Assert.Throws<InvalidOperationException>(() => MemoryPool<PoolItem>.Acquire(), "越过后没有拦下超额取用");
                Assert.AreEqual(limit, Info<PoolItem>().UsingCount, "被拒绝的取用仍占了一个槽");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            // 归还一格后又能取：上限拦的是"在外数量"，不是"取用总量"。
            MemoryPool<PoolItem>.Release(items[0]);
            items[0] = MemoryPool<PoolItem>.Acquire();
            Assert.AreEqual(limit, Info<PoolItem>().UsingCount);

            for (int i = 0; i < limit; i++)
            {
                MemoryPool<PoolItem>.Release(items[i]);
            }

            MemoryPool<PoolItem>.SetLiveLimit(0);
        }

        [Test]
        public void LiveLimitZeroKeepsUnboundedBehaviour()
        {
            MemoryPool<PoolItem>.SetLiveLimit(0);
            List<PoolItem> items = new List<PoolItem>();
            for (int i = 0; i < 200; i++)
            {
                items.Add(MemoryPool<PoolItem>.Acquire());
            }

            Assert.AreEqual(200, Info<PoolItem>().UsingCount);
            Assert.AreEqual(0, Info<PoolItem>().LiveLimit);
            foreach (PoolItem item in items)
            {
                MemoryPool<PoolItem>.Release(item);
            }

            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
        }

        [Test]
        public void NegativeLiveLimitIsTreatedAsUnlimited()
        {
            MemoryPool<PoolItem>.SetLiveLimit(-8);
            Assert.AreEqual(0, Info<PoolItem>().LiveLimit, "负数上限没被归一到 0");
            List<PoolItem> items = new List<PoolItem>();
            for (int i = 0; i < 64; i++)
            {
                items.Add(MemoryPool<PoolItem>.Acquire());
            }

            foreach (PoolItem item in items)
            {
                MemoryPool<PoolItem>.Release(item);
            }
        }

        [Test]
        public void MaxUsingCountIsHighWaterNotCurrentValue()
        {
            MemoryPool<PoolItem>.ResetStats();
            List<PoolItem> items = new List<PoolItem>();
            for (int i = 0; i < 10; i++)
            {
                items.Add(MemoryPool<PoolItem>.Acquire());
            }

            PoolItem retained = items[9];
            for (int i = 0; i < 9; i++)
            {
                MemoryPool<PoolItem>.Release(items[i]);
            }

            items.Clear();
            MemoryPoolInfo info = Info<PoolItem>();
            Assert.AreEqual(1, info.UsingCount, "瞬时在外量不对");
            Assert.AreEqual(10, info.MaxUsingCount, "高水位没记住院子只在峰值时高");

            // 清零统计时高水位按当前在外量重起，否则正在漏还的池会被看着"干净"。
            MemoryPool<PoolItem>.ResetStats();
            info = Info<PoolItem>();
            Assert.AreEqual(1, info.MaxUsingCount, "清零统计时高水位应落到当前在外量");
            Assert.AreEqual(0, info.AcquireCount);

            MemoryPool<PoolItem>.Release(retained);
        }

        [Test]
        public void ValidateStructurePassesOnHealthyPool()
        {
            MemoryPool<PoolItem>.SetCapacity(96, 256);
            WarmChurn();
            Assert.IsNull(MemoryPool<PoolItem>.ValidateStructure(), "健康池被自检判出问题");
            Assert.IsNull(MemoryPoolRegistry.ValidateAll(), "全局自检在健康状态下报了东西");
        }

        [Test]
        public void ValidateStructureReportsCounterTampering()
        {
            WarmChurn();
            Assert.IsNull(MemoryPool<PoolItem>.ValidateStructure());

            int real = MemoryPool<PoolItem>.s_FreeCount;
            MemoryPool<PoolItem>.s_FreeCount = real + 5;
            try
            {
                string error = MemoryPool<PoolItem>.ValidateStructure();
                Assert.IsNotNull(error, "计数与页内合计失配没被测出来");
                StringAssert.Contains("s_FreeCount", error);

                string report = MemoryPoolRegistry.ValidateAll();
                Assert.IsNotNull(report, "全局自检没汇到问题");
                StringAssert.Contains(typeof(PoolItem).FullName, report);
            }
            finally
            {
                MemoryPool<PoolItem>.s_FreeCount = real;
            }

            Assert.IsNull(MemoryPool<PoolItem>.ValidateStructure(), "还原后仍报错，说明自检读到的是脏状态");
        }

        [Test]
        public void CallbackExceptionBatchIsCappedButStillComplete()
        {
            const int count = 40;
            InvalidOperationException cause = new InvalidOperationException("evict");
            PoolItem[] items = new PoolItem[count];
            // 自己数调用次数：PoolItem.OnEvict 是"先回调后计数"，抛出时它自己的计数不会再涨。
            int evictCalls = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    items[i] = MemoryPool<PoolItem>.Acquire();
                    items[i].OnEviction = () =>
                    {
                        evictCalls++;
                        throw cause;
                    };
                }

                for (int i = 0; i < count; i++)
                {
                    MemoryPool<PoolItem>.Release(items[i]);
                }

                Assert.AreEqual(count, MemoryPool<PoolItem>.UnusedCount, "未先填满空闲量");
                AggregateException error = Assert.Throws<AggregateException>(() => MemoryPool<PoolItem>.Shrink(0));

                // 采集上限只削"列出来多少条"，不削"办完多少事"：整批 40 只仍要全部驱逐完。
                Assert.AreEqual(MemoryPoolRegistry.MaxCollectedCallbackExceptions + 1, error.InnerExceptions.Count,
                    "采集上限没生效");
                StringAssert.Contains("省略", error.InnerExceptions[error.InnerExceptions.Count - 1].Message);
                Assert.AreEqual(0, MemoryPool<PoolItem>.UnusedCount, "被削掉的采集让修剪也没走完");
                Assert.AreEqual(count, evictCalls, "有对象的 OnEvict 没被叫到");
            }
            finally
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] != null)
                    {
                        items[i].OnEviction = null;
                    }
                }
            }
        }

        [Test]
        public void TickAllReportsMaintenanceFaultBeforeRethrowing()
        {
            PoolItem first = MemoryPool<PoolItem>.Acquire();
            first.OnEviction = () => throw new InvalidOperationException("evict");
            MemoryPool<PoolItem>.Release(first);
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.LowMemory;

            LogAssert.ignoreFailingMessages = true;
            try
            {
                // 开发期：边界先合并报一条带身份的 Fatal，再按分级上抛。
                // 只有一个池失败时上抛的是那条原始异常本身（聚合只在多失败时合成），断类型要按这个来。
                Exception fault = Assert.Throws<InvalidOperationException>(() => Tick(), "TickAll 没有把池的维护故障上抛");
                StringAssert.Contains("OnEvict() failed", fault.Message);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.AreEqual(0, Info<PoolItem>().UnusedCount, "边界收口把该剪的没剪完");
            first.OnEviction = null;
        }

        [Test]
        public void NativeMetadataIsReleasedForTeardownWhenNothingIsLeased()
        {
            WarmChurn();
            Assert.Greater(Info<PoolItem>().PageCapacity, 0, "没先攒出非托管元数据");

            Assert.IsTrue(MemoryPoolRegistry.TryReleaseAllNativeMetadataForTeardown(), "无外借时收口被拒");
            Assert.AreEqual(0, Info<PoolItem>().PageCapacity, "收口没释放页元数据");

            // 收口之后池必须还能用：页与元数据都是按需重长的。
            MemoryPool<PoolItem>.Release(MemoryPool<PoolItem>.Acquire());
            Assert.AreEqual(1, Info<PoolItem>().UnusedCount);
        }

        [Test]
        public void TeardownReleaseRefusesWhileObjectsAreLeased()
        {
            WarmChurn();
            PoolItem retained = MemoryPool<PoolItem>.Acquire();
            int pagesBefore = Info<PoolItem>().PageCapacity;
            Assert.Greater(pagesBefore, 0);

            // 有对象在外时宁可不回收：释放元数据后那次归还会往已释放内存里写。
            Assert.IsFalse(MemoryPoolRegistry.TryReleaseAllNativeMetadataForTeardown(), "外借状态下不该收口");
            Assert.AreEqual(pagesBefore, Info<PoolItem>().PageCapacity, "被拒的收口仍动了页元数据");

            MemoryPool<PoolItem>.Release(retained);
        }

        private void WarmChurn()
        {
            List<PoolItem> held = new List<PoolItem>();
            for (int round = 0; round < 40; round++)
            {
                int size = 1 + round % 17;
                for (int i = 0; i < size; i++)
                {
                    held.Add(MemoryPool<PoolItem>.Acquire());
                }

                MemoryPoolRegistry.TickAll(++Frame);
                foreach (PoolItem item in held)
                {
                    MemoryPool<PoolItem>.Release(item);
                }

                held.Clear();
                MemoryPoolRegistry.TickAll(++Frame);
            }
        }
    }
}
