using System;
using System.Diagnostics;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;

namespace GameTool
{
    public class MemoryPoolBenchmark
    {
        #region 测试类型 [TEST TYPES]

        private sealed class BenchMemory : MemoryObject
        {
            public int Value;

            public override void Clear()
            {
                Value = 0;
            }
        }

        private sealed class EvictableMemory : MemoryObject, IPoolEvictable
        {
            public static int ClearCount;
            public static int EvictCount;

            public override void Clear()
            {
                ClearCount++;
            }

            public void OnEvict()
            {
                EvictCount++;
            }
        }

        private sealed class TombstoneMemory : MemoryObject, IPoolEvictable
        {
            public static int ClearCount;
            public static int EvictCount;

            public override void Clear()
            {
                ClearCount++;
            }

            public void OnEvict()
            {
                EvictCount++;
            }
        }

        private sealed class ThrowingClearMemory : MemoryObject
        {
            public static bool ThrowOnClear;

            public override void Clear()
            {
                if (ThrowOnClear)
                {
                    throw new InvalidOperationException("clear failed");
                }
            }
        }

        private sealed class ThrowingEvictMemory : MemoryObject, IPoolEvictable
        {
            public static bool ThrowOnEvict;

            public override void Clear() { }

            public void OnEvict()
            {
                if (ThrowOnEvict)
                {
                    throw new InvalidOperationException("evict failed");
                }
            }
        }

        private sealed class ReentryMemory : MemoryObject
        {
            public static bool ReenterOnClear;

            public override void Clear()
            {
                if (ReenterOnClear)
                {
                    MemoryPool.Acquire<ReentryMemory>();
                }
            }
        }

        private sealed class CrossPoolMemoryA : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class CrossPoolMemoryB : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class MultiTypeA : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class MultiTypeB : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class MultiTypeC : MemoryObject
        {
            public override void Clear() { }
        }

        private sealed class DynamicMemory : MemoryObject
        {
            public override void Clear() { }
        }

        private abstract class AbstractMemory : MemoryObject { }

        private sealed class PrivateCtorMemory : MemoryObject
        {
            private PrivateCtorMemory() { }
            public override void Clear() { }
        }

        #endregion

        #region 辅助方法 [UTILITIES]

        private const int PageSize = 32;
        private BenchMemory[] _buffer = new BenchMemory[32768];
        private MemoryPoolInfo[] _infoBuffer = Array.Empty<MemoryPoolInfo>();

        [SetUp]
        public void SetUp()
        {
            MemoryPool.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            MemoryPool.ClearAll();
        }

        private MemoryPoolInfo GetInfo(Type targetType)
        {
            int count = MemoryPool.Count;
            if (_infoBuffer.Length < count)
            {
                _infoBuffer = new MemoryPoolInfo[count];
            }

            int actual = MemoryPool.GetAllMemoryPoolInfos(_infoBuffer);
            for (int i = 0; i < actual; i++)
            {
                if (_infoBuffer[i].Type == targetType)
                {
                    return _infoBuffer[i];
                }
            }

            return default;
        }

        private void WarmPool<T>(int count) where T : MemoryObject, new()
        {
            MemoryPool.Add<T>(count);
            int startFrame = 10000;
            int maxFrames = Math.Max(1, count + 16);
            for (int i = 0; i < maxFrames && MemoryPool<T>.UnusedCount < count; i++)
            {
                MemoryPoolRegistry.TickAll(startFrame + i);
            }
        }

        #endregion

        #region 1a. Phase 行为 [PHASE BEHAVIOR]

        [Test]
        public void LowMemoryPhaseBudget_ZeroGrowthAndAggressiveEviction()
        {
            EMemoryPoolPhase previous = MemoryPoolRegistry.Phase;
            try
            {
                MemoryPool<BenchMemory>.ClearAll();
                MemoryPoolRegistry.Phase = EMemoryPoolPhase.LowMemory;
                MemoryPool<BenchMemory>.Add(32);
                for (int i = 0; i < 8; i++)
                {
                    MemoryPoolRegistry.TickAll(50000 + i);
                }

                MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                Assert.AreEqual(0, info.UnusedCount, "LowMemory phase created free reserve");
            }
            finally
            {
                MemoryPoolRegistry.Phase = previous;
                MemoryPool<BenchMemory>.ClearAll();
            }
        }

        [Test]
        public void LoadingPhaseGrowthBudget_LargeBudgetImmediateGrowth()
        {
            EMemoryPoolPhase previous = MemoryPoolRegistry.Phase;
            try
            {
                MemoryPool<BenchMemory>.ClearAll();
                MemoryPoolRegistry.Phase = EMemoryPoolPhase.Loading;
                MemoryPool<BenchMemory>.Add(8);
                MemoryPoolInfo afterAdd = GetInfo(typeof(BenchMemory));
                Assert.GreaterOrEqual(afterAdd.UnusedCount, 8, "Loading phase did not grow immediately");

                MemoryPoolRegistry.Phase = EMemoryPoolPhase.Background;
                WarmPool<BenchMemory>(16);
                MemoryPool<BenchMemory>.Shrink(0);
                MemoryPoolInfo afterShrink = GetInfo(typeof(BenchMemory));
                Assert.Less(afterShrink.UnusedCount, 16, "Background phase did not evict");
            }
            finally
            {
                MemoryPoolRegistry.Phase = previous;
                MemoryPool<BenchMemory>.ClearAll();
            }
        }

        #endregion

        #region 1b. Native 元数据生命周期 [NATIVE METADATA LIFECYCLE]

        [Test]
        public void TrimNativeRespectsLease_DoesNotFreeWhileLeased()
        {
            MemoryPool<BenchMemory>.ClearAll();
            BenchMemory leased = MemoryPool.Acquire<BenchMemory>();

            MemoryPool.TrimNativeMetadata<BenchMemory>();

            MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
            Assert.AreEqual(1, info.UsingCount, "Trim native released a leased object");
            Assert.Greater(info.PageCapacity, 0, "Trim native freed pages while a lease was live");

            MemoryPool.Release(leased);
            MemoryPool.TrimNativeMetadata<BenchMemory>();

            info = GetInfo(typeof(BenchMemory));
            Assert.AreEqual(0, info.UsingCount, "Trim native after release left objects in use");
            Assert.AreEqual(0, info.PageCapacity, "Trim native after release did not free pages");
            MemoryPool<BenchMemory>.ClearAll();
        }

        [Test]
        public void PendingNativeClearOnLastRelease_ClearsAfterLastRelease()
        {
            MemoryPool<BenchMemory>.ClearAll();
            BenchMemory leased = MemoryPool.Acquire<BenchMemory>();
            MemoryPool<BenchMemory>.ClearAll();
            MemoryPoolInfo afterClear = GetInfo(typeof(BenchMemory));
            Assert.AreEqual(1, afterClear.UsingCount, "Clear all with lease should keep the leased object");

            MemoryPool.Release(leased);

            MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
            Assert.AreEqual(0, info.UsingCount, "Pending native clear left object in use");
            Assert.AreEqual(0, info.UnusedCount, "Pending native clear retained unused objects");
            Assert.AreEqual(0, info.PageCapacity, "Pending native clear did not free pages");
            MemoryPool<BenchMemory>.ClearAll();
        }

        [Test]
        public void AutoTrimNativeAfterIdle_ReleasesAfterIdleThreshold()
        {
            int prevShort = MemoryPool.ShortDecayStartFrames;
            int prevLong = MemoryPool.LongDecayStartFrames;
            int prevZero = MemoryPool.ZeroFreeReserveStartFrames;
            int prevUnschedule = MemoryPool.UnscheduleIdleFrames;
            int prevAutoTrim = MemoryPool.AutoTrimNativeMetadataFrames;
            try
            {
                MemoryPool.ShortDecayStartFrames = 4;
                MemoryPool.LongDecayStartFrames = 8;
                MemoryPool.ZeroFreeReserveStartFrames = 8;
                MemoryPool.UnscheduleIdleFrames = 16;
                MemoryPool.AutoTrimNativeMetadataFrames = 24;
                MemoryPool<BenchMemory>.ClearAll();
                MemoryPool<BenchMemory>.SetCapacity(16, 32);
                WarmPool<BenchMemory>(8);
                MemoryPool<BenchMemory>.Shrink(0);
                for (int i = 0; i < 8 && MemoryPool<BenchMemory>.UnusedCount > 0; i++)
                {
                    MemoryPoolRegistry.TickAll(70000 + i);
                }

                for (int frame = 0; frame < 40; frame++)
                {
                    MemoryPoolRegistry.TickAll(71000 + frame);
                }

                MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                Assert.AreEqual(0, info.UsingCount, "Auto trim left objects in use");
                Assert.AreEqual(0, info.UnusedCount, "Auto trim left unused objects");
                Assert.AreEqual(0, info.PageCapacity, "Auto trim did not release native pages");
            }
            finally
            {
                MemoryPool.ShortDecayStartFrames = prevShort;
                MemoryPool.LongDecayStartFrames = prevLong;
                MemoryPool.ZeroFreeReserveStartFrames = prevZero;
                MemoryPool.UnscheduleIdleFrames = prevUnschedule;
                MemoryPool.AutoTrimNativeMetadataFrames = prevAutoTrim;
                MemoryPool<BenchMemory>.ClearAll();
            }
        }

        #endregion

        #region 1c. 异常安全 [EXCEPTION SAFETY]

        [Test]
        public void ClearCallbackExceptionRollback_KeepsObjectLeased()
        {
            ThrowingClearMemory.ThrowOnClear = true;
            MemoryPool<ThrowingClearMemory>.ClearAll();
            ThrowingClearMemory item = MemoryPool.Acquire<ThrowingClearMemory>();

            Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(item),
                "Clear exception was swallowed");

            MemoryPoolInfo info = GetInfo(typeof(ThrowingClearMemory));
            Assert.AreEqual(1, info.UsingCount, "Clear exception did not keep object leased");

            ThrowingClearMemory.ThrowOnClear = false;
            MemoryPool.Release(item);
            MemoryPool<ThrowingClearMemory>.ClearAll();
        }

        [Test]
        public void EvictCallbackException_DoesNotCorruptState()
        {
            ThrowingEvictMemory.ThrowOnEvict = true;
            const int hardCapacity = 4;
            MemoryPool<ThrowingEvictMemory>.ClearAll();
            MemoryPool<ThrowingEvictMemory>.SetCapacity(hardCapacity, hardCapacity);
            ThrowingEvictMemory[] items = new ThrowingEvictMemory[hardCapacity + 1];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = MemoryPool.Acquire<ThrowingEvictMemory>();
            }

            for (int i = 0; i < hardCapacity; i++)
            {
                MemoryPool.Release(items[i]);
            }

            Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(items[hardCapacity]),
                "Evict exception was swallowed");

            MemoryPoolInfo info = GetInfo(typeof(ThrowingEvictMemory));
            Assert.AreEqual(0, info.UsingCount, "Evict exception left object in use");
            Assert.AreEqual(hardCapacity, info.UnusedCount, "Evict exception corrupted free reserve");

            ThrowingEvictMemory.ThrowOnEvict = false;
            MemoryPool<ThrowingEvictMemory>.ClearAll();
        }

        [Test]
        public void CallbackReentryGuard_ThrowsOnReentry()
        {
            ReentryMemory.ReenterOnClear = true;
            MemoryPool<ReentryMemory>.ClearAll();
            ReentryMemory item = MemoryPool.Acquire<ReentryMemory>();

            Assert.Throws<InvalidOperationException>(() => MemoryPool.Release(item),
                "Callback reentry was accepted");

            ReentryMemory.ReenterOnClear = false;
            MemoryPool.Release(item);
            MemoryPool<ReentryMemory>.ClearAll();
        }

        #endregion

        #region 1d. 硬上限溢出驱逐 + IPoolEvictable [HARD CAP EVICTION]

        [Test]
        public void ReleaseOverHardEvicts_OverflowObjectEvictedWithCallback()
        {
            EvictableMemory.ClearCount = 0;
            EvictableMemory.EvictCount = 0;
            const int hardCapacity = 4;
            const int itemCount = hardCapacity + 1;
            MemoryPool<EvictableMemory>.ClearAll();
            MemoryPool<EvictableMemory>.SetCapacity(hardCapacity, hardCapacity);
            EvictableMemory[] items = new EvictableMemory[itemCount];
            for (int i = 0; i < itemCount; i++)
            {
                items[i] = MemoryPool.Acquire<EvictableMemory>();
            }

            for (int i = 0; i < itemCount; i++)
            {
                MemoryPool.Release(items[i]);
            }

            MemoryPoolInfo info = GetInfo(typeof(EvictableMemory));
            Assert.AreEqual(itemCount, EvictableMemory.ClearCount, "Release over hard did not clear all objects");
            Assert.AreEqual(1, EvictableMemory.EvictCount, "Release over hard did not evict overflow object");
            Assert.AreEqual(hardCapacity, info.UnusedCount, "Release over hard retained wrong free count");
            MemoryPool<EvictableMemory>.ClearAll();
        }

        [Test]
        public void FreeEvictCallback_EvictsFreeWithoutClear()
        {
            EvictableMemory.ClearCount = 0;
            EvictableMemory.EvictCount = 0;
            MemoryPool<EvictableMemory>.ClearAll();
            WarmPool<EvictableMemory>(1);

            MemoryPool<EvictableMemory>.Shrink(0);

            Assert.AreEqual(0, EvictableMemory.ClearCount, "Free evict should not call Clear");
            Assert.Greater(EvictableMemory.EvictCount, 0, "Free evict did not call OnEvict");
            MemoryPool<EvictableMemory>.ClearAll();
        }

        #endregion

        #region 1e. Tombstone 页行为 [TOMBSTONE PAGE]

        [Test]
        public void TombstoneLeasedRelease_EvictsLeasedObjectAndReleasesPage()
        {
            TombstoneMemory.ClearCount = 0;
            TombstoneMemory.EvictCount = 0;
            MemoryPool<TombstoneMemory>.ClearAll();
            TombstoneMemory item = MemoryPool.Acquire<TombstoneMemory>();
            MemoryPool<TombstoneMemory>.ClearAll();

            MemoryPool.Release(item);

            Assert.AreEqual(1, TombstoneMemory.ClearCount, "Tombstone leased release did not call Clear once");
            Assert.AreEqual(1, TombstoneMemory.EvictCount, "Tombstone leased release did not call OnEvict once");
            MemoryPoolInfo info = GetInfo(typeof(TombstoneMemory));
            Assert.AreEqual(0, info.UsingCount, "Tombstone leased release left object in use");
            Assert.AreEqual(0, info.PageCapacity, "Tombstone leased release did not free page storage");
            MemoryPool<TombstoneMemory>.ClearAll();
        }

        #endregion

        #region 1f. 跨池释放拒绝 [CROSS-POOL REJECT]

        [Test]
        public void CrossPoolReleaseReject_ThrowsForWrongPool()
        {
            MemoryPool<CrossPoolMemoryA>.ClearAll();
            MemoryPool<CrossPoolMemoryB>.ClearAll();
            CrossPoolMemoryA item = MemoryPool.Acquire<CrossPoolMemoryA>();

            Assert.Throws<InvalidOperationException>(
                () => MemoryPool<CrossPoolMemoryB>.Release((CrossPoolMemoryB)(object)item),
                "Cross-pool release was accepted");

            MemoryPool.Release(item);
            MemoryPool<CrossPoolMemoryA>.ClearAll();
            MemoryPool<CrossPoolMemoryB>.ClearAll();
        }

        #endregion

        #region 1g. 页边界复用 [PAGE BOUNDARY REUSE]

        [Test]
        public void PageBoundaryReuse_CrossPageAcquireReleaseNoExtraCreate()
        {
            int count = 96; // 3 pages worth
            MemoryPool<BenchMemory>.ClearAll();
            if (_buffer.Length < count)
            {
                _buffer = new BenchMemory[count];
            }

            for (int i = 0; i < count; i++)
            {
                _buffer[i] = MemoryPool.Acquire<BenchMemory>();
            }

            for (int i = 0; i < count; i++)
            {
                MemoryPool.Release(_buffer[i]);
                _buffer[i] = null;
            }

            MemoryPoolRegistry.TickAll(UnityEngine.Time.frameCount);

            MemoryPoolInfo before = GetInfo(typeof(BenchMemory));
            Assert.GreaterOrEqual(before.PageCapacity, count, "Page boundary did not allocate enough page capacity");

            for (int i = 0; i < count; i++)
            {
                _buffer[i] = MemoryPool.Acquire<BenchMemory>();
            }

            MemoryPoolInfo after = GetInfo(typeof(BenchMemory));
            Assert.AreEqual(before.CreateCount, after.CreateCount, "Page boundary reuse created extra objects");

            for (int i = 0; i < count; i++)
            {
                MemoryPool.Release(_buffer[i]);
                _buffer[i] = null;
            }

            MemoryPool<BenchMemory>.ClearAll();
        }

        #endregion

        #region 1h. 波浪式突发抗抖动 [WAVE BURST ANTI-THRASH]

        [Test]
        public void WaveBurstAntiThrash_StableAfterAlternatingBursts()
        {
            int count = Math.Min(128, _buffer.Length);
            MemoryPool<BenchMemory>.ClearAll();
            MemoryPool<BenchMemory>.SetCapacity(count, count << 1);

            for (int wave = 0; wave < 8; wave++)
            {
                int waveSize = (wave & 1) == 0 ? count : count >> 2;
                for (int i = 0; i < waveSize; i++)
                {
                    _buffer[i] = MemoryPool.Acquire<BenchMemory>();
                }

                for (int i = 0; i < waveSize; i++)
                {
                    MemoryPool.Release(_buffer[i]);
                    _buffer[i] = null;
                }

                for (int frame = 0; frame < 12; frame++)
                {
                    MemoryPoolRegistry.TickAll(20000 + wave * 16 + frame);
                }
            }

            MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
            Assert.GreaterOrEqual(info.PageCapacity, info.UnusedCount, "Wave burst page capacity smaller than unused count");
            Assert.Greater(info.UnusedCount, 0, "Wave burst failed to retain reserve");
            MemoryPool<BenchMemory>.ClearAll();
        }

        #endregion

        #region 1i. 多类型并发 Tick [MULTI-TYPE CONCURRENT TICK]

        [Test]
        public void MultiTypeActiveQueue_AllTypesTick()
        {
            int count = 32;
            MemoryPool<MultiTypeA>.ClearAll();
            MemoryPool<MultiTypeB>.ClearAll();
            MemoryPool<MultiTypeC>.ClearAll();
            MemoryPool<MultiTypeA>.SetCapacity(count, count << 1);
            MemoryPool<MultiTypeB>.SetCapacity(count, count << 1);
            MemoryPool<MultiTypeC>.SetCapacity(count, count << 1);

            MultiTypeA[] bufA = new MultiTypeA[count];
            MultiTypeB[] bufB = new MultiTypeB[count];
            MultiTypeC[] bufC = new MultiTypeC[count];

            for (int i = 0; i < count; i++)
            {
                bufA[i] = MemoryPool.Acquire<MultiTypeA>();
                bufB[i] = MemoryPool.Acquire<MultiTypeB>();
                bufC[i] = MemoryPool.Acquire<MultiTypeC>();
            }

            for (int i = 0; i < count; i++)
            {
                MemoryPool.Release(bufA[i]);
                MemoryPool.Release(bufB[i]);
                MemoryPool.Release(bufC[i]);
            }

            for (int frame = 0; frame < 16; frame++)
            {
                MemoryPoolRegistry.TickAll(30000 + frame);
            }

            Assert.Greater(GetInfo(typeof(MultiTypeA)).UnusedCount, 0, "Type A did not tick");
            Assert.Greater(GetInfo(typeof(MultiTypeB)).UnusedCount, 0, "Type B did not tick");
            Assert.Greater(GetInfo(typeof(MultiTypeC)).UnusedCount, 0, "Type C did not tick");
            MemoryPool<MultiTypeA>.ClearAll();
            MemoryPool<MultiTypeB>.ClearAll();
            MemoryPool<MultiTypeC>.ClearAll();
        }

        #endregion

        #region 1j. 缓存句柄热路径 [CACHED HANDLE HOT PATH]

        [Test]
        public void CachedHandleHotPath_AcquireReleaseViaHandle()
        {
            MemoryPool<BenchMemory>.ClearAll();
            WarmPool<BenchMemory>(16);
            MemoryPoolHandle handle = MemoryPool.GetHandle(typeof(BenchMemory));
            Assert.IsTrue(handle.IsValid, "Cached handle is invalid");

            for (int i = 0; i < 1000; i++)
            {
                MemoryObject item = handle.Acquire();
                handle.Release(item);
            }

            Assert.Pass("Cached handle hot path completed without errors");
            MemoryPool<BenchMemory>.ClearAll();
        }

        #endregion

        #region 1k. 信息缓冲区零分配 [INFO BUFFER NO ALLOC]

        [Test]
        public void InfoBufferNoAlloc_ReturnsCorrectCount()
        {
            MemoryPool.Acquire<BenchMemory>();
            int count = MemoryPool.Count;
            Assert.GreaterOrEqual(count, 1);

            MemoryPoolInfo[] buffer = new MemoryPoolInfo[count];
            int actual = MemoryPool.GetAllMemoryPoolInfos(buffer);
            Assert.AreEqual(count, actual, "Info count mismatch");
            Assert.AreEqual(typeof(BenchMemory), buffer[0].Type);
        }

        [Test]
        public void InfoBufferUndersized_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => MemoryPool.GetAllMemoryPoolInfos(Array.Empty<MemoryPoolInfo>()));
        }

        [Test]
        public void InfoBufferNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => MemoryPool.GetAllMemoryPoolInfos(null));
        }

        #endregion

        #region 1l. 空释放安全 [NULL RELEASE SAFETY]

        [Test]
        public void NullReleaseNoop_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MemoryPool.Release((MemoryObject)null));
        }

        [Test]
        public void ReleaseMemoryObjectOwnerPath_ValidObjectReleased()
        {
            MemoryPool<BenchMemory>.ClearAll();
            BenchMemory item = MemoryPool.Acquire<BenchMemory>();
            item.Value = 23;

            MemoryPool.Release((MemoryObject)item);

            Assert.AreEqual(0, item.Value, "MemoryObject release did not clear owned object");
            MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
            Assert.AreEqual(0, info.UsingCount, "MemoryObject release left object in use");
            MemoryPool<BenchMemory>.ClearAll();
        }

        #endregion

        #region 1m. 动态类型 API [DYNAMIC TYPE API]

        [Test]
        public void DynamicTypeAcquireRelease_MaterializesPool()
        {
            MemoryPool<DynamicMemory>.ClearAll();
            MemoryObject memory = MemoryPool.Acquire(typeof(DynamicMemory));
            MemoryPool.Release(memory);

            MemoryPoolInfo info = GetInfo(typeof(DynamicMemory));
            Assert.Greater(info.AcquireCount, 0, "Dynamic type acquire did not materialize pool");
            Assert.Greater(info.ReleaseCount, 0, "Dynamic type release did not return through owner handle");
            MemoryPool<DynamicMemory>.ClearAll();
        }

        [Test]
        public void DynamicTypeAcquire_InvalidType_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => MemoryPool.Acquire(null));
            Assert.Throws<InvalidOperationException>(() => MemoryPool.Acquire(typeof(string)));
            Assert.Throws<InvalidOperationException>(() => MemoryPool.Acquire(typeof(AbstractMemory)));
            Assert.Throws<InvalidOperationException>(() => MemoryPool.Acquire(typeof(PrivateCtorMemory)));
        }

        #endregion

        #region 1n. 空闲收缩与已租借对象 [IDLE SHRINK WHILE LEASED]

        [Test]
        public void IdleShrinkWhileLeased_DoesNotDropLeasedObject()
        {
            int prevShort = MemoryPool.ShortDecayStartFrames;
            int prevLong = MemoryPool.LongDecayStartFrames;
            int prevZero = MemoryPool.ZeroFreeReserveStartFrames;
            int prevUnschedule = MemoryPool.UnscheduleIdleFrames;
            try
            {
                MemoryPool.ShortDecayStartFrames = 8;
                MemoryPool.LongDecayStartFrames = 16;
                MemoryPool.ZeroFreeReserveStartFrames = 16;
                MemoryPool.UnscheduleIdleFrames = 128;
                MemoryPool<BenchMemory>.ClearAll();
                MemoryPool<BenchMemory>.SetCapacity(32, 64);
                WarmPool<BenchMemory>(16);

                BenchMemory leased = MemoryPool.Acquire<BenchMemory>();
                int unusedAfterLease = MemoryPool<BenchMemory>.UnusedCount;

                for (int frame = 0; frame < 80; frame++)
                {
                    MemoryPoolRegistry.TickAll(60000 + frame);
                }

                MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
                Assert.AreEqual(1, info.UsingCount, "Idle shrink while leased dropped the leased object");
                Assert.Less(info.UnusedCount, unusedAfterLease, "Idle shrink while leased did not reduce unused objects");
                MemoryPool.Release(leased);
            }
            finally
            {
                MemoryPool.ShortDecayStartFrames = prevShort;
                MemoryPool.LongDecayStartFrames = prevLong;
                MemoryPool.ZeroFreeReserveStartFrames = prevZero;
                MemoryPool.UnscheduleIdleFrames = prevUnschedule;
                MemoryPool<BenchMemory>.ClearAll();
            }
        }

        #endregion

        #region 2. 性能基准 [PERFORMANCE BENCHMARKS]

        private const int HotLoopCount = 100000;
        private const int BurstSize = 4096;
        private const int ExtremeBurstSize = 32768;
        private const int AdaptiveFrameCount = 420;
        private const int WaveCount = 24;

        /// <summary>
        /// 热路径 Acquire/Release 循环，断言零 GC 分配。
        /// </summary>
        [Test]
        public void AcquireReleaseHotLoop_ZeroGcAlloc()
        {
            MemoryPool<BenchMemory>.ClearAll();
            MemoryPool<BenchMemory>.SetCapacity(256, 1024);
            WarmPool<BenchMemory>(256);

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < HotLoopCount; i++)
            {
                BenchMemory item = MemoryPool<BenchMemory>.Acquire();
                item.Value = i;
                MemoryPool<BenchMemory>.Release(item);
            }

            sw.Stop();
            long allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            Assert.AreEqual(0, allocDelta, "Hot loop allocated {0} bytes on GC heap", allocDelta);
            UnityEngine.Debug.Log($"[MemoryPoolBenchmark] AcquireReleaseHotLoop: {HotLoopCount} iterations, {sw.Elapsed.TotalMilliseconds:F2}ms, GC alloc={allocDelta}");
            MemoryPool<BenchMemory>.ClearAll();
        }

        /// <summary>
        /// Facade 泛型 API 热路径循环，断言零 GC 分配。
        /// </summary>
        [Test]
        public void GenericApiHotLoop_ZeroGcAlloc()
        {
            MemoryPool<BenchMemory>.ClearAll();
            MemoryPool<BenchMemory>.SetCapacity(256, 1024);
            WarmPool<BenchMemory>(256);

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < HotLoopCount; i++)
            {
                BenchMemory item = MemoryPool.Acquire<BenchMemory>();
                MemoryPool.Release(item);
            }

            sw.Stop();
            long allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            Assert.AreEqual(0, allocDelta, "Generic API hot loop allocated {0} bytes", allocDelta);
            UnityEngine.Debug.Log($"[MemoryPoolBenchmark] GenericApiHotLoop: {HotLoopCount} iterations, {sw.Elapsed.TotalMilliseconds:F2}ms, GC alloc={allocDelta}");
            MemoryPool<BenchMemory>.ClearAll();
        }

        /// <summary>
        /// 缓存句柄热路径循环，断言零 GC 分配。
        /// </summary>
        [Test]
        public void CachedHandleHotPath_ZeroGcAlloc()
        {
            MemoryPool<BenchMemory>.ClearAll();
            WarmPool<BenchMemory>(256);
            MemoryPoolHandle handle = MemoryPool.GetHandle(typeof(BenchMemory));
            Assert.IsTrue(handle.IsValid, "Cached handle is invalid");

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < HotLoopCount; i++)
            {
                MemoryObject item = handle.Acquire();
                handle.Release(item);
            }

            sw.Stop();
            long allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            Assert.AreEqual(0, allocDelta, "Cached handle hot path allocated {0} bytes", allocDelta);
            UnityEngine.Debug.Log($"[MemoryPoolBenchmark] CachedHandleHotPath: {HotLoopCount} iterations, {sw.Elapsed.TotalMilliseconds:F2}ms, GC alloc={allocDelta}");
            MemoryPool<BenchMemory>.ClearAll();
        }

        /// <summary>
        /// GetAllMemoryPoolInfos 缓冲区零分配验证。
        /// </summary>
        [Test]
        public void InfoBufferNoAlloc_ZeroGcAlloc()
        {
            MemoryPool.Acquire<BenchMemory>();
            int count = MemoryPool.Count;
            MemoryPoolInfo[] buffer = new MemoryPoolInfo[count];

            long allocBefore = GC.GetAllocatedBytesForCurrentThread();

            int actual = MemoryPool.GetAllMemoryPoolInfos(buffer);

            long allocDelta = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            Assert.AreEqual(count, actual);
            Assert.AreEqual(0, allocDelta, "GetAllMemoryPoolInfos allocated {0} bytes", allocDelta);
            MemoryPool<BenchMemory>.ClearAll();
        }

        /// <summary>
        /// 突发填充后 Tick，验证池保留量 > 0 且页容量充足。
        /// </summary>
        [Test]
        public void AdaptiveBurstFill_RetainsReserve()
        {
            int count = Math.Min(BurstSize, _buffer.Length);
            MemoryPool<BenchMemory>.ClearAll();
            MemoryPool<BenchMemory>.SetCapacity(Math.Max(64, count >> 1), count << 1);

            var sw = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
                _buffer[i] = MemoryPool<BenchMemory>.Acquire();

            for (int i = 0; i < count; i++)
            {
                MemoryPool<BenchMemory>.Release(_buffer[i]);
                _buffer[i] = null;
            }

            for (int frame = 0; frame < AdaptiveFrameCount; frame++)
                MemoryPoolRegistry.TickAll(frame);

            sw.Stop();

            MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
            Assert.Greater(info.UnusedCount, 0, "Adaptive fill did not keep reserve");
            Assert.GreaterOrEqual(info.PageCapacity, info.UnusedCount, "Page capacity smaller than unused count");
            UnityEngine.Debug.Log($"[MemoryPoolBenchmark] AdaptiveBurstFill: burst={count}, {sw.Elapsed.TotalMilliseconds:F2}ms, reserve={info.UnusedCount}, pages={info.PageCapacity}");
            MemoryPool<BenchMemory>.ClearAll();
        }

        /// <summary>
        /// 波浪式交替大小突发后 Tick，验证抗抖动（保留量 > 0 且稳定）。
        /// </summary>
        [Test]
        public void WaveBurstAntiThrash_StableReserve()
        {
            int count = Math.Min(BurstSize, _buffer.Length);
            MemoryPool<BenchMemory>.ClearAll();
            MemoryPool<BenchMemory>.SetCapacity(count, count << 1);

            var sw = Stopwatch.StartNew();

            for (int wave = 0; wave < WaveCount; wave++)
            {
                int waveSize = (wave & 1) == 0 ? count : count >> 2;
                for (int i = 0; i < waveSize; i++)
                    _buffer[i] = MemoryPool<BenchMemory>.Acquire();

                for (int i = 0; i < waveSize; i++)
                {
                    MemoryPool<BenchMemory>.Release(_buffer[i]);
                    _buffer[i] = null;
                }

                for (int frame = 0; frame < 12; frame++)
                    MemoryPoolRegistry.TickAll(20000 + wave * 16 + frame);
            }

            sw.Stop();

            MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
            Assert.GreaterOrEqual(info.PageCapacity, info.UnusedCount, "Wave burst page capacity smaller than unused count");
            Assert.Greater(info.UnusedCount, 0, "Wave burst failed to retain reserve");
            UnityEngine.Debug.Log($"[MemoryPoolBenchmark] WaveBurstAntiThrash: waves={WaveCount}, {sw.Elapsed.TotalMilliseconds:F2}ms, reserve={info.UnusedCount}, pages={info.PageCapacity}");
            MemoryPool<BenchMemory>.ClearAll();
        }

        /// <summary>
        /// 极端单次突发，验证硬上限约束。
        /// </summary>
        [Test]
        public void ExtremeSingleBurst_RespectsHardCap()
        {
            int count = Math.Min(ExtremeBurstSize, _buffer.Length);
            int hardCapacity = count;
            MemoryPool<BenchMemory>.ClearAll();
            MemoryPool<BenchMemory>.SetCapacity(Math.Max(128, count >> 2), hardCapacity);

            var sw = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
                _buffer[i] = MemoryPool<BenchMemory>.Acquire();

            for (int i = 0; i < count; i++)
            {
                MemoryPool<BenchMemory>.Release(_buffer[i]);
                _buffer[i] = null;
            }

            sw.Stop();

            MemoryPoolInfo info = GetInfo(typeof(BenchMemory));
            Assert.AreEqual(count, info.UnusedCount, "Extreme burst did not keep released objects under hard cap");
            Assert.GreaterOrEqual(info.PageCapacity, info.UnusedCount, "Extreme burst page capacity smaller than unused count");
            UnityEngine.Debug.Log($"[MemoryPoolBenchmark] ExtremeSingleBurst: burst={count}, {sw.Elapsed.TotalMilliseconds:F2}ms, reserve={info.UnusedCount}, pages={info.PageCapacity}");
            MemoryPool<BenchMemory>.ClearAll();
        }

        /// <summary>
        /// 多类型并发 Acquire/Release + Tick，验证各类型保留量 > 0。
        /// </summary>
        [Test]
        public void MultiTypeActiveQueue_AllTypesRetainReserve()
        {
            int count = 2048;
            MemoryPool<MultiTypeA>.ClearAll();
            MemoryPool<MultiTypeB>.ClearAll();
            MemoryPool<MultiTypeC>.ClearAll();
            MemoryPool<MultiTypeA>.SetCapacity(count, count << 1);
            MemoryPool<MultiTypeB>.SetCapacity(count, count << 1);
            MemoryPool<MultiTypeC>.SetCapacity(count, count << 1);

            MultiTypeA[] bufA = new MultiTypeA[count];
            MultiTypeB[] bufB = new MultiTypeB[count];
            MultiTypeC[] bufC = new MultiTypeC[count];

            var sw = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
            {
                bufA[i] = MemoryPool.Acquire<MultiTypeA>();
                bufB[i] = MemoryPool.Acquire<MultiTypeB>();
                bufC[i] = MemoryPool.Acquire<MultiTypeC>();
            }

            for (int i = 0; i < count; i++)
            {
                MemoryPool.Release(bufA[i]);
                MemoryPool.Release(bufB[i]);
                MemoryPool.Release(bufC[i]);
            }

            for (int frame = 0; frame < AdaptiveFrameCount; frame++)
                MemoryPoolRegistry.TickAll(30000 + frame);

            sw.Stop();

            Assert.Greater(GetInfo(typeof(MultiTypeA)).UnusedCount, 0, "Type A did not tick");
            Assert.Greater(GetInfo(typeof(MultiTypeB)).UnusedCount, 0, "Type B did not tick");
            Assert.Greater(GetInfo(typeof(MultiTypeC)).UnusedCount, 0, "Type C did not tick");
            UnityEngine.Debug.Log($"[MemoryPoolBenchmark] MultiTypeActiveQueue: types=3, count={count}, {sw.Elapsed.TotalMilliseconds:F2}ms");
            MemoryPool<MultiTypeA>.ClearAll();
            MemoryPool<MultiTypeB>.ClearAll();
            MemoryPool<MultiTypeC>.ClearAll();
        }

        #endregion
    }
}
