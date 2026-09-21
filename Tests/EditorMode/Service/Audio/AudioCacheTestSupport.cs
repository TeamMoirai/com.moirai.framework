using System;
using System.Collections.Generic;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// Clip 缓存测试台：可控的 <see cref="IAudioClipLeaseSource"/> 假件 + 台账不变量断言。
    /// <para>假件把「同地址实际向后端发起的加载次数」与「尚未归还后端的租约数」显式记账，
    /// 异步完成由测试手动放行（<see cref="CompleteNext"/>），以便断言迟到回调的世代校验。</para>
    /// </summary>
    internal sealed class AudioCacheTestSupport : IAudioClipLeaseSource, IDisposable
    {
        private readonly Dictionary<string, int> _loads = new Dictionary<string, int>();
        private readonly Queue<PendingLoad> _pending = new Queue<PendingLoad>();

        public AudioCacheTestSupport(int capacity = 128, float ttl = 30f,
            AudioCachePolicy defaultPolicy = AudioCachePolicy.Ttl)
        {
            Cache = new AudioClipCache();
            Cache.Configure(this, capacity, ttl, defaultPolicy);
        }

        /// <summary>被测缓存。</summary>
        public AudioClipCache Cache { get; }

        /// <summary>已交给缓存、尚未归还后端的租约数。</summary>
        public int LiveHandles;

        /// <summary>true 时加载一律失败（后端缺地址）。</summary>
        public bool FailLoads;

        /// <summary>true 时异步加载不立即回调，改由 <see cref="CompleteNext"/> 放行。</summary>
        public bool ManualAsync;

        /// <summary>挂起中的异步加载数。</summary>
        public int PendingCount => _pending.Count;

        public int LoadCount(string address) => _loads.TryGetValue(address, out var count) ? count : 0;

        bool IAudioClipLeaseSource.TryAcquire(string address, out AudioClipLease lease)
        {
            BumpLoad(address);
            lease = FailLoads ? default : MakeLease();
            return !FailLoads;
        }

        void IAudioClipLeaseSource.AcquireAsync(string address, System.Threading.CancellationToken cancellationToken,
            Action<AudioClipLease> completed)
        {
            BumpLoad(address);
            if (ManualAsync)
            {
                _pending.Enqueue(new PendingLoad(address, completed));
                return;
            }

            completed(FailLoads ? default : MakeLease());
        }

        /// <summary>
        /// 放行一个挂起的异步加载（按发起顺序）。
        /// </summary>
        /// <param name="fail">true 时回调空租约，模拟后端失败。</param>
        /// <param name="address">覆盖回调携带的地址；缺省沿用发起时的地址。</param>
        public void CompleteNext(bool fail = false, string address = null)
        {
            if (_pending.Count == 0) throw new InvalidOperationException("没有待放行的异步加载。");

            var pending = _pending.Dequeue();
            pending.Completed(fail ? default : MakeLease());
        }

        /// <summary>放行全部挂起的异步加载。</summary>
        public void CompleteAll(bool fail = false)
        {
            while (_pending.Count > 0)
            {
                CompleteNext(fail);
            }
        }

        /// <summary>取一个已在缓存中的条目（不改变引用计数）。</summary>
        public AudioClipCacheEntry Entry(string address)
        {
            Assert.IsTrue(Cache.TryGetEntry(address, out var entry), $"缓存中应有 {address}");
            return entry;
        }

        public void Dispose()
        {
            Cache.Dispose();
        }

        private void BumpLoad(string address)
        {
            _loads.TryGetValue(address, out var count);
            _loads[address] = count + 1;
        }

        private AudioClipLease MakeLease()
        {
            // AudioClip 不是 ScriptableObject，只能用 Create 工厂造一个空载波（1ms 单声道）用于身份比对
            var clip = AudioClip.Create("test-clip", 1, 1, 44100, false);
            return new AudioClipLease(clip, new TestLease(this));
        }

        private readonly struct PendingLoad
        {
            public PendingLoad(string address, Action<AudioClipLease> completed)
            {
                Address = address;
                Completed = completed;
            }

            public string Address { get; }
            public Action<AudioClipLease> Completed { get; }
        }

        private sealed class TestLease : IDisposable
        {
            private readonly AudioCacheTestSupport _owner;
            private bool _disposed;

            public TestLease(AudioCacheTestSupport owner)
            {
                _owner = owner;
                owner.LiveHandles++;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.LiveHandles--;
            }
        }

        #region 台账不变量 [LEDGER INVARIANTS]

        /// <summary>
        /// 遍历缓存内部链表的不变量断言：两条双向链自洽无环、LRU 链上的条目必为「无引用且非 Pin 非 Loading」
        /// 且按最后使用时间递增、<c>Count</c> 与 All 链长度一致。用于锁死驱逐与槽位复用的记账。
        /// </summary>
        public void CheckInvariants()
        {
            var inAll = new HashSet<AudioClipCacheEntry>();

            int allCount = 0;
            for (var entry = Cache.FirstEntry; entry != null; entry = entry.AllNext)
            {
                if (!inAll.Add(entry)) Assert.Fail($"台账：All 链出现环或重复条目 {entry.Address}");
                allCount++;
            }

            Assert.AreEqual(Cache.Count, allCount, "台账：Count 与 All 链长度不符");

            // LRU 是 All 的子集，同一条目合法地同时挂在两条链上，因此判环要各用各的集合
            var inLru = new HashSet<AudioClipCacheEntry>();
            AudioClipCacheEntry prev = null;
            int lruCount = 0;
            for (var entry = Cache.FirstLruEntry; entry != null; entry = entry.LruNext)
            {
                if (!inLru.Add(entry)) Assert.Fail($"台账：LRU 链出现环或重复条目 {entry.Address}");
                if (!inAll.Contains(entry)) Assert.Fail($"台账：LRU 链上的条目不在 All 链中（{entry.Address}）");
                Assert.IsTrue(entry.InLru, $"台账：LRU 链上的条目 InLru 为 false（{entry.Address}）");
                Assert.AreEqual(0, entry.RefCount, $"台账：LRU 链上的条目仍有引用（{entry.Address}）");
                Assert.IsFalse(entry.Pinned, $"台账：Pin 条目不应在 LRU 链上（{entry.Address}）");
                Assert.IsFalse(entry.Loading, $"台账：Loading 中的条目不应在 LRU 链上（{entry.Address}）");
                if (prev != null)
                {
                    Assert.LessOrEqual(prev.LastUseTime, entry.LastUseTime,
                        $"台账：LRU 链未按最后使用时间递增（{prev.Address} -> {entry.Address}）");
                }

                prev = entry;
                lruCount++;
            }

            Assert.LessOrEqual(lruCount, allCount, "台账：LRU 链条目数超过缓存总数");
        }

        #endregion 台账不变量 [LEDGER INVARIANTS]
    }
}
