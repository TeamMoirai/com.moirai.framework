using System.Threading;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Moirai.Atropos.Tests.EditorMode.Audio
{
    /// <summary>
    /// <see cref="AudioClipCache"/> 语义回归：单飞加载、引用计数、LRU/TTL/Pin 驱逐、容量上界、
    /// lowMemory 回收、迟到回调作废，以及关停后的租约全部归还。
    /// </summary>
    [TestFixture]
    public class AudioClipCacheTests
    {
        private const string A = "Audio/Sfx/Confirm";
        private const string B = "Audio/Sfx/LevelUp";
        private const string C = "Audio/Sfx/Coin";

        private AudioCacheTestSupport _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new AudioCacheTestSupport();
        }

        [TearDown]
        public void TearDown()
        {
            var fixture = _fixture;
            _fixture = null;
            if (fixture == null) return;

            fixture.Dispose();
            // 关停必须把每条租约都还给后端；此处不为零就是所有权记账漏了
            Assert.AreEqual(0, fixture.LiveHandles, "TearDown：仍有未归还的 clip 租约");
        }

        #region 取得与单飞 [ACQUIRE & SINGLE-FLIGHT]

        [Test]
        public void Preload_SyncLoad_AcquiresOnceAndStaysLoaded()
        {
            Assert.IsTrue(_fixture.Cache.Preload(A), "同步预加载应成功");

            Assert.AreEqual(1, _fixture.LoadCount(A));
            Assert.AreEqual(1, _fixture.LiveHandles, "一条地址只应持有一条租约");
            Assert.AreEqual(1, _fixture.Cache.Count);
            Assert.IsTrue(_fixture.Entry(A).IsLoaded);
            Assert.AreEqual(1, _fixture.Cache.PoolView.Count, "留池条目应投影到 AssetHandlePool 视图");
            _fixture.CheckInvariants();
        }

        [Test]
        public void Preload_SameAddressTwice_DoesNotAcquireAgain()
        {
            _fixture.Cache.Preload(A);
            Assert.IsTrue(_fixture.Cache.Preload(A));

            Assert.AreEqual(1, _fixture.LoadCount(A), "已加载条目不得二次向后端取租约");
            Assert.AreEqual(1, _fixture.LiveHandles);
            _fixture.CheckInvariants();
        }

        [Test]
        public void Preload_NonePolicy_LoadsThenDropsImmediately()
        {
            // None 的语义是「用完即弃」，而预加载本身不构成引用：加载完成即无人在用，条目当场回收
            Assert.IsTrue(_fixture.Cache.Preload(A, AudioCachePolicy.None));

            Assert.AreEqual(1, _fixture.LoadCount(A));
            Assert.AreEqual(0, _fixture.Cache.Count);
            Assert.AreEqual(0, _fixture.LiveHandles, "None 策略不得把租约留在缓存里");
            Assert.AreEqual(0, _fixture.Cache.PoolView.Count);
        }

        [Test]
        public void PreloadAsync_ConcurrentRequests_SingleFlightServesEveryWaiter()
        {
            _fixture.ManualAsync = true;

            bool first = false;
            bool second = false;
            _fixture.Cache.PreloadAsync(A, AudioCachePolicy.Pin, ok => first = ok);
            _fixture.Cache.PreloadAsync(A, AudioCachePolicy.Pin, ok => second = ok);

            Assert.AreEqual(1, _fixture.LoadCount(A), "同地址并发请求应合并为一次加载");
            Assert.AreEqual(1, _fixture.Cache.LoadingCount);
            Assert.IsFalse(first);
            Assert.IsFalse(second);

            _fixture.CompleteNext();

            Assert.IsTrue(first);
            Assert.IsTrue(second);
            Assert.AreEqual(1, _fixture.LiveHandles);
            Assert.AreEqual(1, _fixture.Cache.Count);
            Assert.IsTrue(_fixture.Entry(A).Pinned, "Default 以外的显式策略应原样落到条目上");
            _fixture.CheckInvariants();
        }

        [Test]
        public void PreloadAsync_Failure_NotifiesWaitersFalseAndLeavesNoEntry()
        {
            _fixture.ManualAsync = true;
            int calls = 0;
            bool last = true;
            _fixture.Cache.PreloadAsync(A, AudioCachePolicy.Ttl, ok =>
            {
                calls++;
                last = ok;
            });

            _fixture.CompleteNext(fail: true);

            Assert.AreEqual(1, calls, "等待者应恰好被通知一次");
            Assert.IsFalse(last, "加载失败必须以 false 通知等待者");
            Assert.IsFalse(_fixture.Cache.TryGetEntry(A, out _), "加载失败的地址不该留下占位条目");
            Assert.AreEqual(0, _fixture.LiveHandles);
            _fixture.CheckInvariants();
        }

        [Test]
        public void Preload_BackendMissingAddress_ReturnsFalseWithoutLeaking()
        {
            _fixture.FailLoads = true;

            Assert.IsFalse(_fixture.Cache.Preload(A));

            Assert.AreEqual(0, _fixture.Cache.Count);
            Assert.AreEqual(0, _fixture.LiveHandles);
        }

        [Test]
        public void Preload_EmptyAddress_Rejected()
        {
            Assert.IsFalse(_fixture.Cache.Preload(null));
            Assert.IsFalse(_fixture.Cache.Preload(string.Empty));
            Assert.AreEqual(0, _fixture.Cache.Count);
        }

        [Test]
        public void RequestClip_TwoAgents_SingleFlightForTheSameAddress()
        {
            _fixture.ManualAsync = true;
            var first = new AudioAgent();
            var second = new AudioAgent();

            Assert.IsTrue(_fixture.Cache.RequestClip(A, true, AudioCachePolicy.Ttl, first, first.LoadGeneration));
            Assert.IsTrue(_fixture.Cache.RequestClip(A, true, AudioCachePolicy.Ttl, second, second.LoadGeneration));

            Assert.AreEqual(1, _fixture.LoadCount(A), "同地址多个声部只该向后端取一次租约");
            Assert.AreEqual(1, _fixture.Cache.LoadingCount);

            _fixture.CompleteNext();

            // 两个声部都未在 Loading（EditMode 里没有被 Category 驱动），接管被拒后条目按默认策略留池
            Assert.AreEqual(1, _fixture.Cache.Count);
            Assert.AreEqual(1, _fixture.LiveHandles, "被拒的接管不得留下额外引用");
            _fixture.CheckInvariants();
        }

        [Test]
        public void RequestClip_NullAgentOrAddress_IsRefused()
        {
            Assert.IsFalse(_fixture.Cache.RequestClip(A, true, AudioCachePolicy.Ttl, null, 1));
            Assert.IsFalse(_fixture.Cache.RequestClip(string.Empty, true, AudioCachePolicy.Ttl, new AudioAgent(), 1));
            Assert.AreEqual(0, _fixture.Cache.Count);
            Assert.AreEqual(0, _fixture.LiveHandles);
        }

        #endregion 取得与单飞 [ACQUIRE & SINGLE-FLIGHT]

        #region 引用与驱逐 [REFERENCES & EVICTION]

        [Test]
        public void RetainedEntry_SurvivesNonForcedClearAndTtl()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            var entry = _fixture.Entry(A);
            _fixture.Cache.Retain(entry);

            _fixture.Cache.ClearCache(force: true);

            Assert.IsTrue(_fixture.Cache.TryGetEntry(A, out _), "在引用的条目不可被驱逐");
            Assert.AreEqual(1, _fixture.LiveHandles);
            Assert.IsFalse(entry.InLru, "引用中的条目不应挂在 LRU 链上");
            _fixture.CheckInvariants();

            _fixture.Cache.Release(entry);

            Assert.AreEqual(1, _fixture.LiveHandles, "默认 Ttl 策略下归零应留池而非释放");
            Assert.IsTrue(entry.InLru, "归零后应回到 LRU 链等待到期");
            _fixture.CheckInvariants();
        }

        [Test]
        public void Release_BelowZero_KeepsLedgerAndIsReported()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Pin);
            var entry = _fixture.Entry(A);
            _fixture.Cache.Retain(entry);
            _fixture.Cache.Release(entry);

            LogAssert.ignoreFailingMessages = true;
            try
            {
                _fixture.Cache.Release(entry);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.AreEqual(0, entry.RefCount, "重复归还不得把引用计数打成负数");
            Assert.IsTrue(_fixture.Cache.TryGetEntry(A, out _));
            _fixture.CheckInvariants();
        }

        [Test]
        public void Capacity_EvictsLeastRecentlyUsedAndReleasesItsLease()
        {
            _fixture = new AudioCacheTestSupport(capacity: 2);

            // Preload 默认 Pin（常驻、不参与驱逐），这里要的是「用后留池但可被挤掉」的 Ttl
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, AudioCachePolicy.Ttl);
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl); // A 被再次取用，B 成为最久未用
            _fixture.Cache.Preload(C, AudioCachePolicy.Ttl);

            Assert.IsTrue(_fixture.Cache.TryGetEntry(A, out _), "A 刚被取用，不该被驱逐");
            Assert.IsFalse(_fixture.Cache.TryGetEntry(B, out _), "B 应作为最久未用被驱逐");
            Assert.IsTrue(_fixture.Cache.TryGetEntry(C, out _));
            Assert.AreEqual(2, _fixture.Cache.Count);
            Assert.AreEqual(2, _fixture.LiveHandles, "被驱逐条目的租约必须归还后端");
            _fixture.CheckInvariants();
        }

        [Test]
        public void Capacity_AllPinned_RefusesNewAddressInsteadOfGrowing()
        {
            _fixture = new AudioCacheTestSupport(capacity: 2);

            Assert.IsTrue(_fixture.Cache.Preload(A, AudioCachePolicy.Pin));
            Assert.IsTrue(_fixture.Cache.Preload(B, AudioCachePolicy.Pin));
            Assert.IsFalse(_fixture.Cache.Preload(C, AudioCachePolicy.Pin), "满载且无可驱逐对象时应判负");

            Assert.AreEqual(2, _fixture.Cache.Count, "不得为超载地址扩到容量之外");
            Assert.AreEqual(0, _fixture.LoadCount(C), "判负的地址不应真的去取资源");
            Assert.AreEqual(2, _fixture.LiveHandles);
            _fixture.CheckInvariants();
        }

        [Test]
        public void UpgradePolicy_PinWinsAndNeverDowngrades()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.None);
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            Assert.IsFalse(_fixture.Entry(A).Pinned);

            _fixture.Cache.Preload(A, AudioCachePolicy.Pin);
            Assert.IsTrue(_fixture.Entry(A).Pinned);

            _fixture.Cache.Preload(A, AudioCachePolicy.None);
            Assert.IsTrue(_fixture.Entry(A).Pinned, "策略只升不降：None 不得摘掉 Pin");
        }

        #endregion 引用与驱逐 [REFERENCES & EVICTION]

        #region TTL 与 lowMemory [TTL & LOW MEMORY]

        [Test]
        public void Ttl_ExpiresIdleEntry_AndTouchRenews()
        {
            const float ttl = 0.25f;
            _fixture = new AudioCacheTestSupport(capacity: 8, ttl: ttl);

            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            Thread.Sleep(80);
            _fixture.Cache.Tick();
            Assert.IsTrue(_fixture.Cache.TryGetEntry(A, out _), "未过期的条目不该被回收");

            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl); // 续期
            Thread.Sleep(120);
            _fixture.Cache.Tick();
            Assert.IsTrue(_fixture.Cache.TryGetEntry(A, out _), "续期后 TTL 应重新计时");

            Thread.Sleep(200);
            _fixture.Cache.Tick();

            Assert.IsFalse(_fixture.Cache.TryGetEntry(A, out _), "TTL 到期应被回收");
            Assert.AreEqual(0, _fixture.LiveHandles);
            _fixture.CheckInvariants();
        }

        [Test]
        public void Ttl_NonPositive_DisablesTimeEviction()
        {
            _fixture = new AudioCacheTestSupport(capacity: 8, ttl: 0f);

            _fixture.Cache.Preload(A);
            Thread.Sleep(60);
            _fixture.Cache.Tick();

            Assert.IsTrue(_fixture.Cache.TryGetEntry(A, out _));
        }

        [Test]
        public void LowMemory_ClearsIdleTtl_KeepsPinnedAndInUse()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, AudioCachePolicy.Pin);
            _fixture.Cache.Preload(C, AudioCachePolicy.Ttl);
            var inUse = _fixture.Entry(C);
            _fixture.Cache.Retain(inUse);

            _fixture.Cache.OnLowMemory();

            Assert.IsFalse(_fixture.Cache.TryGetEntry(A, out _), "空闲 TTL 条目应被低内存回收");
            Assert.IsTrue(_fixture.Cache.TryGetEntry(B, out _), "Pin 条目不受 lowMemory 影响");
            Assert.IsTrue(_fixture.Cache.TryGetEntry(C, out _), "在播条目不受 lowMemory 影响");
            Assert.AreEqual(2, _fixture.LiveHandles);
            _fixture.CheckInvariants();
        }

        #endregion TTL 与 lowMemory [TTL & LOW MEMORY]

        #region 卸载与关停 [UNLOAD & SHUTDOWN]

        [Test]
        public void Unload_PinnedNeedsForce_InUseNeedsRelease()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Pin);

            Assert.IsFalse(_fixture.Cache.Unload(A), "Pin 条目非 force 不卸");

            var entry = _fixture.Entry(A);
            _fixture.Cache.Retain(entry);
            Assert.IsFalse(_fixture.Cache.Unload(A, force: true), "在播引用连 force 也不得卸");

            _fixture.Cache.Release(entry);
            Assert.IsTrue(_fixture.Cache.Unload(A, force: true));

            Assert.IsFalse(_fixture.Cache.TryGetEntry(A, out _));
            Assert.AreEqual(0, _fixture.LiveHandles);
        }

        [Test]
        public void ClearCache_NonForced_KeepsPinned()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Ttl);
            _fixture.Cache.Preload(B, AudioCachePolicy.Pin);

            _fixture.Cache.ClearCache();

            Assert.IsFalse(_fixture.Cache.TryGetEntry(A, out _));
            Assert.IsTrue(_fixture.Cache.TryGetEntry(B, out _));
            Assert.AreEqual(1, _fixture.Cache.PinnedCount);

            _fixture.Cache.ClearCache(force: true);

            Assert.AreEqual(0, _fixture.Cache.Count);
            Assert.AreEqual(0, _fixture.LiveHandles);
            Assert.AreEqual(0, _fixture.Cache.PoolView.Count);
        }

        [Test]
        public void LateCompletion_AfterShutdown_ReleasesLeaseWithoutResurrecting()
        {
            _fixture.ManualAsync = true;
            _fixture.Cache.PreloadAsync(A, AudioCachePolicy.Ttl, null);
            Assert.AreEqual(1, _fixture.PendingCount);

            _fixture.Dispose();

            Assert.AreEqual(0, _fixture.Cache.Count);

            // 关停后迟到的完成回调：租约必须当场归还，且不得把已作废的条目写回缓存
            _fixture.CompleteNext();

            Assert.AreEqual(0, _fixture.LiveHandles, "迟到回调携带的租约必须被释放");
            Assert.AreEqual(0, _fixture.Cache.Count);
            _fixture = null; // 已关停，避开 TearDown 的重复 Dispose
        }

        [Test]
        public void Dispose_ReleasesEveryLiveHandle()
        {
            _fixture.Cache.Preload(A, AudioCachePolicy.Pin);
            _fixture.Cache.Preload(B);
            var inUse = _fixture.Entry(B);
            _fixture.Cache.Retain(inUse);

            Assert.AreEqual(2, _fixture.LiveHandles);

            _fixture.Cache.Dispose();

            Assert.AreEqual(0, _fixture.Cache.Count);
            Assert.AreEqual(0, _fixture.LiveHandles, "关停必须连在播引用持有的租约一并收回");
            _fixture = null;
        }

        #endregion 卸载与关停 [UNLOAD & SHUTDOWN]

        #region 条目复用 [ENTRY RECYCLING]

        [Test]
        public void StaleCompletion_AfterEntryRecycled_CannotTouchTheNewOwner()
        {
            // 条目对象归还全局池后会被下一个缓存复用；在途回调抓着的是旧身份，
            // 既不能污染新主人的状态，自带的租约也必须当场归还。
            var first = new AudioCacheTestSupport(capacity: 4);
            first.ManualAsync = true;
            first.Cache.PreloadAsync(A, AudioCachePolicy.Ttl, null);
            Assert.AreEqual(1, first.PendingCount);

            first.Dispose();

            var second = new AudioCacheTestSupport(capacity: 4);
            Assert.IsTrue(second.Cache.Preload(A, AudioCachePolicy.Ttl));
            int entriesBefore = second.Cache.Count;

            first.CompleteNext();

            Assert.AreEqual(0, first.LiveHandles, "迟到回调自带的租约必须当场归还");
            Assert.AreEqual(entriesBefore, second.Cache.Count, "迟到回调不得改变另一个缓存的条目");
            Assert.AreEqual(1, second.LiveHandles, "新主人的租约不该被动过");
            Assert.IsTrue(second.Entry(A).IsLoaded);
            second.CheckInvariants();

            second.Dispose();
        }

        #endregion 条目复用 [ENTRY RECYCLING]
    }
}
