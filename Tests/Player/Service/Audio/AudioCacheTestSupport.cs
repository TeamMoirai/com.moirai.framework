using System;
using System.Collections.Generic;
using System.Threading;
using Moirai.Atropos.Audio;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// Clip 缓存测试台（Player 程序集本地版）：可控的 <see cref="IAudioClipLeaseSource"/> 假件 + 租约台账。
    /// <para>与 EditorMode 同名支撑同构（跨程序集不可共享，重复属可接受形态）：把「同地址实际向后端发起的
    /// 加载次数」与「尚未归还后端的租约数」显式记账，供分配基准断言复用。仅覆盖分配基准所需的同步路径。</para>
    /// </summary>
    internal sealed class AudioCacheTestSupport : IAudioClipLeaseSource, IDisposable
    {
        private readonly Dictionary<string, int> _loads = new Dictionary<string, int>();
        private readonly Queue<AudioClipLease> _preparedLeases = new Queue<AudioClipLease>();
        private readonly Queue<TestLease> _recycledHandles = new Queue<TestLease>();

        public AudioCacheTestSupport(int capacity = 128, float ttl = 30f,
            EAudioCachePolicy defaultPolicy = EAudioCachePolicy.Ttl,
            float failureCooldown = AudioClipCache.FailureCooldownSeconds)
        {
            Cache = new AudioClipCache();
            Cache.Configure(this, capacity, ttl, defaultPolicy, failureCooldown);
        }

        /// <summary>被测缓存。</summary>
        public AudioClipCache Cache { get; }

        /// <summary>已交给缓存、尚未归还后端的租约数。</summary>
        public int LiveHandles;

        /// <summary>true 时加载一律失败（后端缺地址）。</summary>
        public bool FailLoads;

        /// <summary>true 时归还的句柄重新投入池中复用（身份比对类基准保持 false）。</summary>
        public bool RecycleLeases;

        public int LoadCount(string address) => _loads.TryGetValue(address, out var count) ? count : 0;

        bool IAudioClipLeaseSource.TryAcquire(string address, out AudioClipLease lease)
        {
            BumpLoad(address);
            lease = FailLoads ? default : MakeLease();
            return !FailLoads;
        }

        void IAudioClipLeaseSource.AcquireAsync(string address, CancellationToken cancellationToken,
            Action<AudioClipLease> completed)
        {
            BumpLoad(address);
            completed(FailLoads ? default : MakeLease());
        }

        /// <summary>取一个已在缓存中的条目（不改变引用计数）。</summary>
        public AudioClipCacheEntry Entry(string address)
        {
            Assert.IsTrue(Cache.TryGetEntry(address, out var entry), $"缓存中应有 {address}");
            return entry;
        }

        /// <summary>预建若干租约：把「测试台自己的 new」挡在被测窗口之外，量到的才全是被测路径的分配。</summary>
        public void PrepareLeases(int count)
        {
            for (int i = 0; i < count; i++) _preparedLeases.Enqueue(CreateLease());
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
            => _preparedLeases.Count > 0 ? _preparedLeases.Dequeue() : CreateLease();

        private AudioClipLease CreateLease()
        {
            if (_recycledHandles.Count > 0)
            {
                var reused = _recycledHandles.Dequeue();
                reused.Rearm();
                return new AudioClipLease(reused.Clip, reused);
            }

            // AudioClip 只能用工厂造空载波（1 采样单声道）用于身份比对
            var clip = AudioClip.Create("bench-clip", 1, 1, 44100, false);
            return new AudioClipLease(clip, new TestLease(this, clip));
        }

        private sealed class TestLease : IDisposable
        {
            private readonly AudioCacheTestSupport _owner;
            private bool _disposed;

            public TestLease(AudioCacheTestSupport owner, AudioClip clip)
            {
                _owner = owner;
                Clip = clip;
                owner.LiveHandles++;
            }

            public AudioClip Clip { get; }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.LiveHandles--;
                if (_owner.RecycleLeases) _owner._recycledHandles.Enqueue(this);
            }

            /// <summary>重新投入使用；未归还就复用说明句柄被复制到了两处，直接抛而不是静默错账。</summary>
            public void Rearm()
            {
                if (!_disposed) throw new InvalidOperationException("租约尚未归还即重新投入使用。");
                _disposed = false;
                _owner.LiveHandles++;
            }
        }
    }
}
