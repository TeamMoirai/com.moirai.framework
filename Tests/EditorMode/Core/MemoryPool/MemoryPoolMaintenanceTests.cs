using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;
using Mp = Moirai.Atropos.MemoryPool;

namespace Core.MemoryPool
{
    /// <summary>
    /// 维护路径回归：预算、水位、页退役与回收、坏回调隔离，以及"全局入口不得从池回调里重入"。
    /// </summary>
    public sealed class MemoryPoolMaintenanceTests : MemoryPoolFixture
    {
        [Test]
        public void RepeatedConstructorFailuresDoNotConsumeSlots()
        {
            InvalidOperationException cause = new InvalidOperationException("constructor");
            ConstructorItem.Construct = () => throw cause;
            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(CausedBy(CatchThrows(() => MemoryPool<ConstructorItem>.Acquire()), cause));
            }

            Assert.AreEqual(0, Info<ConstructorItem>().UsingCount, "构造失败累积了租约");
            Assert.AreEqual(0, Info<ConstructorItem>().CreateCount, "构造失败累积了创建数");
            Assert.AreEqual(0, Info<ConstructorItem>().PageCapacity, "构造失败白占了页");

            ConstructorItem.Construct = null;
            ConstructorItem[] items = new ConstructorItem[257];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MemoryPool<ConstructorItem>.Acquire();
            }

            for (int i = 0; i < items.Length; i++)
            {
                MemoryPool<ConstructorItem>.Release(items[i]);
            }

            Assert.AreEqual(0, Info<ConstructorItem>().UsingCount);
        }

        [Test]
        public void ConstructorReentryCannotMutateItsPool()
        {
            ConstructorItem.Construct = () => Assert.Throws<InvalidOperationException>(
                () => MemoryPool<ConstructorItem>.Add(1), "构造期可改写本池");
            MemoryPool<ConstructorItem>.Release(MemoryPool<ConstructorItem>.Acquire());
            ConstructorItem.Construct = null;

            Assert.AreEqual(1, Info<ConstructorItem>().UnusedCount, "构造护栏把池搞坏了");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GlobalMaintenanceFromCallbackIsRejectedBeforePartialCleanup(bool fromEvict)
        {
            OtherItem other = MemoryPool<OtherItem>.Acquire();
            MemoryPool<OtherItem>.Release(other);
            PoolItem item = MemoryPool<PoolItem>.Acquire();
            Action callback = () =>
            {
                Assert.Throws<InvalidOperationException>(() => Mp.ClearAll(), "回调内可重入全局 ClearAll");
                Assert.Throws<InvalidOperationException>(() => Mp.TrimAllNativeMetadata(), "回调内可重入全局 Trim");
                Assert.Throws<InvalidOperationException>(() => MemoryPoolRegistry.TickAll(++Frame), "回调内可重入全局 TickAll");
                int softCapacity = Mp.DefaultSoftFreeReserveLimit;
                int hardCapacity = Mp.DefaultHardFreeReserveLimit;
                Assert.Throws<InvalidOperationException>(() => Mp.SetDefaultCapacity(4, 4), "回调内可重入默认容量");
                Assert.AreEqual(softCapacity, Mp.DefaultSoftFreeReserveLimit, "被拒的 SetDefaultCapacity 仍改了默认软上限");
                Assert.AreEqual(hardCapacity, Mp.DefaultHardFreeReserveLimit, "被拒的 SetDefaultCapacity 仍改了默认硬上限");
                Assert.AreEqual(0, other.Evictions, "被拒的全局维护已经把别的池剪了");
            };

            if (fromEvict)
            {
                item.OnEviction = callback;
            }
            else
            {
                item.OnClear = callback;
            }

            MemoryPool<PoolItem>.Release(item);
            MemoryPool<PoolItem>.ClearAll();
            Assert.AreEqual(0, other.Evictions);
        }

        [Test]
        public void ShrinkCompletesRequestedCleanupWhenAnEvictionThrows()
        {
            PoolItem[] items = new PoolItem[12];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MemoryPool<PoolItem>.Acquire();
            }

            InvalidOperationException cause = new InvalidOperationException("evict");
            for (int i = 0; i < items.Length; i++)
            {
                items[i].OnEviction = () => throw cause;
                MemoryPool<PoolItem>.Release(items[i]);
            }

            AggregateException error = Assert.Throws<AggregateException>(() => MemoryPool<PoolItem>.Shrink(0));
            Assert.AreEqual(items.Length, error.Flatten().InnerExceptions.Count, "坏回调之间互相顶掉了");
            foreach (Exception failure in error.Flatten().InnerExceptions)
            {
                Assert.IsInstanceOf<InvalidOperationException>(failure);
                StringAssert.Contains("OnEvict() failed", failure.Message);
                Assert.AreSame(cause, failure.InnerException, "原始原因被丢了");
            }

            Assert.AreEqual(0, Info<PoolItem>().UnusedCount, "抛出后仍有空闲对象没被剪掉");
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
        }

        [TestCase(EMemoryPoolPhase.Boot, 32)]
        [TestCase(EMemoryPoolPhase.Loading, 32)]
        [TestCase(EMemoryPoolPhase.Gameplay, 2)]
        [TestCase(EMemoryPoolPhase.Background, 8)]
        [TestCase(EMemoryPoolPhase.LowMemory, 0)]
        public void ExplicitAddUsesPhaseBudget(EMemoryPoolPhase phase, int budget)
        {
            MemoryPoolRegistry.Phase = phase;
            MemoryPool<PoolItem>.Add(100);
            Assert.AreEqual(budget, Info<PoolItem>().UnusedCount, $"{phase} 阶段单次 Add 建超了预算");

            Tick();
            Assert.AreEqual(budget * 2, Info<PoolItem>().UnusedCount, $"{phase} 阶段后续 Tick 没按同一预算续建");
        }

        [Test]
        public void AddIntMaxValueClampsWithoutOverflow()
        {
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.Loading;
            MemoryPool<PoolItem>.SetCapacity(64, 64);
            MemoryPool<PoolItem>.Add(int.MaxValue);
            Tick(3);

            Assert.AreEqual(64, Info<PoolItem>().UnusedCount, "int.MaxValue 的 Add 没有夹到硬上限");
        }

        [Test]
        public void ExplicitRemoveCancelsQueuedGrowth()
        {
            MemoryPool<PoolItem>.Add(100);
            MemoryPool<PoolItem>.Shrink(0);
            Tick(100);

            Assert.AreEqual(0, Info<PoolItem>().UnusedCount, "被撤销的增长请求仍在后续帧里落地");
        }

        [Test]
        public void LowMemoryEvictsWithinBudgetAndPreservesLeases()
        {
            PoolItem[] items = new PoolItem[81];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MemoryPool<PoolItem>.Acquire();
            }

            for (int i = 0; i < 80; i++)
            {
                MemoryPool<PoolItem>.Release(items[i]);
            }

            MemoryPoolRegistry.Phase = EMemoryPoolPhase.LowMemory;
            Tick();
            Assert.AreEqual(48, Info<PoolItem>().UnusedCount, "低内存首轮没按 32 的驱逐预算剪");
            Assert.AreEqual(1, Info<PoolItem>().UsingCount);

            Tick(2);
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount, "低内存没把空闲储备剪空");
            Assert.AreEqual(0, items[80].Evictions, "在外的对象被驱逐了");

            MemoryPool<PoolItem>.Release(items[80]);
            Tick();
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount, "最后一只归还后又攒出了空闲储备");
        }

        [Test]
        public void IdleExpiryReleasesNativeStorageAndStopsScheduling()
        {
            Mp.ShortDecayStartFrames = 1;
            Mp.LongDecayStartFrames = 2;
            Mp.ZeroFreeReserveStartFrames = 2;
            Mp.UnscheduleIdleFrames = 3;
            Mp.AutoTrimNativeMetadataFrames = 4;
            MemoryPool<PoolItem>.Release(MemoryPool<PoolItem>.Acquire());
            Tick(200);

            Assert.AreEqual(0, Info<PoolItem>().UnusedCount);
            Assert.AreEqual(0, Info<PoolItem>().PageCapacity);
            Assert.AreEqual(-1, Mp.GetHandle(typeof(PoolItem)).Inner.ActiveIndex, "空闲到点后可以继续被排期");
            Assert.AreEqual(0, StaticField<int>(typeof(MemoryPool<PoolItem>), "s_PageCapacity"),
                "PageCapacity 归零可能只是页都进了回收栈，非托管数组没真释放");

            MemoryPool<PoolItem>.Release(MemoryPool<PoolItem>.Acquire());
            Assert.AreEqual(1, Info<PoolItem>().UnusedCount, "自动修剪后池子不能重新长起来");
        }

        [Test]
        public void TrimPreservesActiveLeaseAndThenReclaimsMetadata()
        {
            PoolItem item = MemoryPool<PoolItem>.Acquire();
            object payload = new object();
            item.Payload = payload;

            MemoryPool<PoolItem>.TrimNativeMetadata();
            Assert.AreSame(payload, item.Payload, "带租约修剪时动了对象内容");
            Assert.AreEqual(1, Info<PoolItem>().UsingCount, "带租约修剪把对象收回去了");

            MemoryPool<PoolItem>.Release(item);
            MemoryPool<PoolItem>.TrimNativeMetadata();
            Assert.AreEqual(0, Info<PoolItem>().PageCapacity, "无租约后修剪没释放 Native 元数据");
            Assert.AreEqual(1, item.Evictions);
        }

        [Test]
        public void ClearReleasesEveryEmptyPageWhileOtherPagesAreLeased()
        {
            PoolItem[] items = new PoolItem[1000];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MemoryPool<PoolItem>.Acquire();
            }

            for (int i = 1; i < items.Length; i++)
            {
                MemoryPool<PoolItem>.Release(items[i]);
            }

            MemoryPool<PoolItem>.ClearAll();
            Assert.AreEqual(32, Info<PoolItem>().PageCapacity, "只剩一页有租约时其它空页没被退役");

            MemoryPool<PoolItem>.Release(items[0]);
            Assert.AreEqual(0, Info<PoolItem>().PageCapacity, "最后一只归还后页存储没被释放");
        }

        [Test]
        public void PageRetirementAndReuseCannotStrandFreeSlots()
        {
            PoolItem[] held = new PoolItem[129];
            MemoryPool<PoolItem>.SetCapacity(160, 160);
            for (int round = 0; round < 300; round++)
            {
                for (int i = 0; i < held.Length; i++)
                {
                    held[i] = MemoryPool<PoolItem>.Acquire();
                }

                for (int i = 0; i < held.Length; i++)
                {
                    MemoryPool<PoolItem>.Release(held[i]);
                }

                MemoryPool<PoolItem>.Shrink(round % 33);
                int available = Info<PoolItem>().UnusedCount;
                int created = Info<PoolItem>().CreateCount;
                for (int i = 0; i < available; i++)
                {
                    held[i] = MemoryPool<PoolItem>.Acquire();
                }

                Assert.AreEqual(created, Info<PoolItem>().CreateCount,
                    $"round {round}: 空闲量 {available} 走查不完整，缺的槽位被当成新建补走");

                for (int i = 0; i < available; i++)
                {
                    MemoryPool<PoolItem>.Release(held[i]);
                }
            }
        }

        [Test]
        public void RegistryGrowthSchedulesEveryTypeWithoutARepairScan()
        {
            List<MemoryPoolHandle> handles = new List<MemoryPoolHandle>();
            Type[] arguments =
            {
                typeof(int), typeof(long), typeof(string), typeof(byte), typeof(short),
                typeof(float), typeof(double), typeof(decimal), typeof(DateTime), typeof(Guid)
            };
            foreach (Type first in arguments)
            {
                foreach (Type second in arguments)
                {
                    Type pair = typeof(KeyValuePair<,>).MakeGenericType(first, second);
                    MemoryPoolHandle handle = Mp.GetHandle(typeof(ColdItem<>).MakeGenericType(pair));
                    handles.Add(handle);
                    handle.Release(handle.Acquire());
                }
            }

            try
            {
                Tick(2);
                foreach (MemoryPoolHandle handle in handles)
                {
                    Assert.AreEqual(1, Info(handle).IdleFrames, "有类型在这一轮 Tick 里被漏掉");
                    Assert.GreaterOrEqual(handle.Inner.ActiveIndex, 0, "排期表容量不足导致漏排");
                }
            }
            finally
            {
                foreach (MemoryPoolHandle handle in handles)
                {
                    handle.Inner.Clear();
                }
            }
        }

        [Test]
        public void InfoBufferIsCallerOwnedAndValidated()
        {
            Assert.Throws<ArgumentNullException>(() => Mp.GetAllMemoryPoolInfos(null));
            Assert.Throws<ArgumentException>(() => Mp.GetAllMemoryPoolInfos(Array.Empty<MemoryPoolInfo>()));
            MemoryPoolInfo[] buffer = new MemoryPoolInfo[Mp.Count];
            Assert.AreEqual(buffer.Length, Mp.GetAllMemoryPoolInfos(buffer));
            foreach (MemoryPoolInfo info in buffer)
            {
                Assert.IsNotNull(info.Type);
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NonPositiveRemovalDoesNotOverflowOrEvict(int count)
        {
            MemoryPool<PoolItem>.Release(MemoryPool<PoolItem>.Acquire());
            Mp.Remove<PoolItem>(count);
            Mp.Remove(typeof(PoolItem), count);
            Assert.AreEqual(1, Info<PoolItem>().UnusedCount, $"Remove({count}) 改动了空闲量");

            Mp.Remove<PoolItem>(int.MaxValue);
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount);
        }

        [Test]
        public void GlobalClearPreservesSchedulingOfObjectsCreatedByAnotherPoolsEviction()
        {
            MemoryPoolHandle first = Mp.GetHandle(typeof(PoolItem));
            MemoryPoolHandle second = Mp.GetHandle(typeof(OtherItem));
            Array registry = StaticField<Array>(typeof(MemoryPoolRegistry), "s_HandleValues");
            int firstIndex = -1;
            int secondIndex = -1;
            for (int i = 0; i < registry.Length; i++)
            {
                if (ReferenceEquals(registry.GetValue(i), first.Inner))
                {
                    firstIndex = i;
                }

                if (ReferenceEquals(registry.GetValue(i), second.Inner))
                {
                    secondIndex = i;
                }
            }

            Assert.GreaterOrEqual(firstIndex, 0);
            Assert.GreaterOrEqual(secondIndex, 0);
            MemoryPoolHandle earlier = firstIndex < secondIndex ? first : second;
            MemoryPoolHandle later = firstIndex < secondIndex ? second : first;

            earlier.Release(earlier.Acquire());
            MemoryObject trigger = later.Acquire();
            ((PoolItem)trigger).OnEviction = () => earlier.Release(earlier.Acquire());
            later.Release(trigger);

            Mp.ClearAll();

            Assert.AreEqual(1, Info(earlier).UnusedCount, "别的池在清空过程中新建的空闲对象被抹了");
            Assert.GreaterOrEqual(earlier.Inner.ActiveIndex, 0, "全局 Clear 的批量清排期把新排期的池一起摘了");
            ((PoolItem)trigger).OnEviction = null;
        }

        [Test]
        public void BatchCleanupReportsEveryCallbackFailure()
        {
            PoolItem first = MemoryPool<PoolItem>.Acquire();
            OtherItem second = MemoryPool<OtherItem>.Acquire();
            first.OnEviction = () => throw new InvalidOperationException("first");
            second.OnEviction = () => throw new InvalidOperationException("second");
            MemoryPool<PoolItem>.Release(first);
            MemoryPool<OtherItem>.Release(second);

            AggregateException error = Assert.Throws<AggregateException>(() => Mp.ClearAll());
            Assert.AreEqual(2, error.Flatten().InnerExceptions.Count, "只报了第一个坏回调");
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount);
            Assert.AreEqual(0, Info<OtherItem>().UnusedCount);
        }

        [Test]
        public void TickMaintenanceContinuesWhenOtherPoolsEvictionCallbacksFail()
        {
            Use<ColdItem<MemoryPoolMaintenanceTests>>();
            PoolItem first = MemoryPool<PoolItem>.Acquire();
            OtherItem second = MemoryPool<OtherItem>.Acquire();
            first.OnEviction = () => throw new InvalidOperationException("first");
            second.OnEviction = () => throw new InvalidOperationException("second");
            MemoryPool<PoolItem>.Release(first);
            MemoryPool<OtherItem>.Release(second);
            MemoryPool<ColdItem<MemoryPoolMaintenanceTests>>.Release(
                MemoryPool<ColdItem<MemoryPoolMaintenanceTests>>.Acquire());
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.LowMemory;

            AggregateException error = Assert.Throws<AggregateException>(() => Tick());
            Assert.AreEqual(0, Info<ColdItem<MemoryPoolMaintenanceTests>>().UnusedCount, "别的池抛出后本轮不再维护");
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount);
            Assert.AreEqual(0, Info<OtherItem>().UnusedCount);
            Assert.AreEqual(2, error.Flatten().InnerExceptions.Count);
        }
    }
}
