using System;
using System.Collections.Generic;
using System.Threading;
using Moirai.Atropos;
using NUnit.Framework;
using Mp = Moirai.Atropos.MemoryPool;

namespace Core.MemoryPool
{
    /// <summary>
    /// 归属与生命周期契约：谁借的谁还、还得掉的才还、坏回调不能改账，
    /// 以及跨池 / 跨线程 / 突发溢出这些"账本不能被记乱"的边界。
    /// </summary>
    public sealed class MemoryPoolOwnershipTests : MemoryPoolFixture
    {
        [Test]
        public void ReturnClearsPayloadAndReusesIdentity()
        {
            PoolItem item = MemoryPool<PoolItem>.Acquire();
            item.Payload = new object();
            MemoryPool<PoolItem>.Release(item);

            Assert.IsNull(item.Payload);
            Assert.AreEqual(1, item.Clears);
            Assert.AreSame(item, MemoryPool<PoolItem>.Acquire(), "归还后没有把同一实例再发出去");
            MemoryPool<PoolItem>.Release(item);
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
        }

        [Test]
        public void ConstructorFailureDoesNotAcquireOwnership()
        {
            InvalidOperationException cause = new InvalidOperationException("constructor");
            ConstructorItem.Construct = () => throw cause;

            Exception exception = CatchThrows(() => MemoryPool<ConstructorItem>.Acquire());
            Assert.IsTrue(CausedBy(exception, cause), "构造失败丢掉的不是构造函数自己的异常");
            Assert.AreEqual(0, Info<ConstructorItem>().UsingCount, "构造失败留下了幽灵租约");
            Assert.AreEqual(0, Info<ConstructorItem>().AcquireCount, "构造失败仍记了取用次数");
            Assert.AreEqual(0, Info<ConstructorItem>().CreateCount, "构造失败仍记了创建数");

            ConstructorItem.Construct = null;
            MemoryPool<ConstructorItem>.Release(MemoryPool<ConstructorItem>.Acquire());
            Assert.AreEqual(1, Info<ConstructorItem>().UnusedCount);
        }

        [Test]
        public void ClearFailureKeepsLeaseForRetry()
        {
            PoolItem item = MemoryPool<PoolItem>.Acquire();
            InvalidOperationException cause = new InvalidOperationException("clear");
            item.OnClear = () => throw cause;

            Exception exception = CatchThrows(() => MemoryPool<PoolItem>.Release(item));
            Assert.IsTrue(Mentions(exception, "Clear() failed"), "归还失败没有按 Clear 回调报错上报");
            Assert.IsTrue(CausedBy(exception, cause));
            Assert.AreEqual(1, Info<PoolItem>().UsingCount, "Clear 抛出后对象不该已经在池里");
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount);

            item.OnClear = null;
            MemoryPool<PoolItem>.Release(item);
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
        }

        [Test]
        public void EvictionFailureStillRetiresEveryFreeObject()
        {
            PoolItem first = MemoryPool<PoolItem>.Acquire();
            PoolItem second = MemoryPool<PoolItem>.Acquire();
            InvalidOperationException cause = new InvalidOperationException("evict");
            first.OnEviction = () => throw cause;
            MemoryPool<PoolItem>.Release(first);
            MemoryPool<PoolItem>.Release(second);

            Exception exception = CatchThrows(() => MemoryPool<PoolItem>.ClearAll());
            Assert.IsTrue(Mentions(exception, "OnEvict() failed"), "空闲驱逐失败没有按 OnEvict 上报");
            Assert.IsTrue(CausedBy(exception, cause));
            Assert.AreEqual(1, second.Evictions, "一个坏回调吃掉同页其余对象的驱逐");
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount);
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
        }

        [Test]
        public void ClearRetiresOldLeasesWithoutEvictingNewGeneration()
        {
            PoolItem old = MemoryPool<PoolItem>.Acquire();
            MemoryPool<PoolItem>.ClearAll();

            PoolItem current = MemoryPool<PoolItem>.Acquire();
            MemoryPool<PoolItem>.Release(current);
            MemoryPool<PoolItem>.Release(old);

            Assert.AreEqual(1, old.Evictions, "老租约归还时应当被驱逐");
            Assert.AreEqual(0, current.Evictions, "新一代对象被老租约的归还误伤");
            Assert.AreEqual(1, Info<PoolItem>().UnusedCount);
        }

        [TestCase("Acquire")]
        [TestCase("Release")]
        [TestCase("Add")]
        [TestCase("Shrink")]
        [TestCase("Compact")]
        [TestCase("SetCapacity")]
        [TestCase("ClearAll")]
        [TestCase("Trim")]
        [TestCase("ResetStats")]
        public void SamePoolMutationDuringClearIsRejected(string operation)
        {
            PoolItem item = MemoryPool<PoolItem>.Acquire();
            PoolItem other = MemoryPool<PoolItem>.Acquire();
            Action mutation = operation switch
            {
                "Acquire" => () => MemoryPool<PoolItem>.Acquire(),
                "Release" => () => MemoryPool<PoolItem>.Release(other),
                "Add" => () => MemoryPool<PoolItem>.Add(1),
                "Shrink" => () => MemoryPool<PoolItem>.Shrink(0),
                "Compact" => () => MemoryPool<PoolItem>.Compact(),
                "SetCapacity" => () => MemoryPool<PoolItem>.SetCapacity(4, 4),
                "ClearAll" => () => MemoryPool<PoolItem>.ClearAll(),
                "Trim" => () => MemoryPool<PoolItem>.TrimNativeMetadata(),
                _ => () => MemoryPool<PoolItem>.ResetStats()
            };

            bool rejected = false;
            item.OnClear = () =>
            {
                try
                {
                    mutation();
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
            };
            MemoryPool<PoolItem>.Release(item);
            item.OnClear = null;
            MemoryPool<PoolItem>.Release(other);

            Assert.IsTrue(rejected, $"Clear() 期间 {operation} 未被拒绝");
        }

        [Test]
        public void CrossPoolNestedOwnershipIsAllowed()
        {
            OtherItem child = MemoryPool<OtherItem>.Acquire();
            PoolItem parent = MemoryPool<PoolItem>.Acquire();
            parent.OnClear = () => MemoryPool<OtherItem>.Release(child);
            MemoryPool<PoolItem>.Release(parent);
            parent.OnClear = null;

            Assert.AreEqual(0, Info<OtherItem>().UsingCount, "归还父对象时带出的子对象应当入池");
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
        }

        [Test]
        public void DoubleForeignAndUnownedReturnsAreRejected()
        {
            OtherItem item = MemoryPool<OtherItem>.Acquire();

            Exception foreign = CatchThrows(() => MemoryPool<PoolItem>.Release(item));
            Assert.IsTrue(Mentions(foreign, "belongs to another pool"), "跨池归还未拒绝");

            Mp.Release((MemoryObject)item);
            Exception twice = CatchThrows(() => Mp.Release((MemoryObject)item));
            Assert.IsTrue(Mentions(twice, "not leased"), "二次归还未拒绝");

            Exception unowned = CatchThrows(() => Mp.Release((MemoryObject)new PoolItem()));
            Assert.IsTrue(Mentions(unowned, "no owner pool"), "未入池对象归还未拒绝");

            Assert.AreEqual(0, Info<OtherItem>().UsingCount);
        }

        [Test]
        public void TypeAndHandleApisKeepOwnerIdentity()
        {
            MemoryPoolHandle handle = Mp.GetHandle(typeof(PoolItem));
            MemoryObject item = handle.Acquire();
            handle.Release(item);

            Assert.AreSame(item, handle.Acquire(), "缓存句柄没有复用同一实例");
            handle.Release(item);
            Assert.AreSame(item, Mp.Acquire(typeof(PoolItem)), "Type 入口与句柄入口复用的不是同一实例");
            handle.Release(item);

            Assert.IsTrue(Mentions(CatchThrows(() => default(MemoryPoolHandle).Acquire()), "invalid"));
            Assert.IsTrue(Mentions(CatchThrows(() => Mp.GetHandle(typeof(string))), "must inherit MemoryObject"));
            Assert.IsTrue(Mentions(CatchThrows(() => Mp.GetHandle(typeof(MemoryObject))), "must not be abstract"));
            Assert.Throws<ArgumentNullException>(() => Mp.GetHandle(null));
        }

        [Test]
        public void WorkerThreadCannotMutateAnInitializedPool()
        {
            Exception error = null;
            Thread thread = new Thread(() =>
            {
                try
                {
                    MemoryPool<PoolItem>.Acquire();
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "工作线程卡住");
            Assert.IsTrue(error is InvalidOperationException, "工作线程取用没被主线程断言拦下");
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
        }

        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(4097)]
        public void BurstOverflowRetiresOnlyReturnedObjects(int count)
        {
            MemoryPool<PoolItem>.SetCapacity(4, 4);
            PoolItem[] items = new PoolItem[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = MemoryPool<PoolItem>.Acquire();
            }

            for (int i = 0; i < count; i++)
            {
                MemoryPool<PoolItem>.Release(items[i]);
            }

            Assert.AreEqual(Math.Min(4, count), Info<PoolItem>().UnusedCount, "硬上限没有收住空闲量");
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
            int evicted = 0;
            for (int i = 0; i < count; i++)
            {
                evicted += items[i].Evictions;
            }

            Assert.AreEqual(Math.Max(0, count - 4), evicted, "溢出对象被驱逐的只数不对");
        }

        [Test]
        public void RandomBusinessLifetimesKeepTheOwnershipLedgerBalanced()
        {
            Random random = new Random(314159);
            List<PoolItem> leased = new List<PoolItem>();
            HashSet<PoolItem> held = new HashSet<PoolItem>();
            for (int i = 0; i < 20000; i++)
            {
                if (leased.Count == 0 || random.Next(100) < 55)
                {
                    PoolItem item = MemoryPool<PoolItem>.Acquire();
                    Assert.IsTrue(held.Add(item), "同一实例被重复发放");
                    leased.Add(item);
                }
                else
                {
                    int index = random.Next(leased.Count);
                    PoolItem item = leased[index];
                    MemoryPool<PoolItem>.Release(item);
                    leased[index] = leased[leased.Count - 1];
                    leased.RemoveAt(leased.Count - 1);
                    Assert.IsTrue(held.Remove(item), "归还了并不在外的对象");
                }

                if (i % 113 == 0)
                {
                    MemoryPool<PoolItem>.ClearAll();
                }

                if (i % 71 == 0)
                {
                    MemoryPool<PoolItem>.Shrink(3);
                }

                if (i % 17 == 0)
                {
                    Tick();
                }

                Assert.AreEqual(leased.Count, Info<PoolItem>().UsingCount, $"op {i} 在外账目失真");
            }

            for (int i = 0; i < leased.Count; i++)
            {
                MemoryPool<PoolItem>.Release(leased[i]);
            }

            MemoryPool<PoolItem>.ClearAll();
            Assert.AreEqual(0, Info<PoolItem>().UsingCount);
            Assert.AreEqual(0, Info<PoolItem>().UnusedCount);
        }
    }
}
