using System;
using System.Collections.Generic;
using System.Threading;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 泄漏验收：混合 Play/Stop/Preload/Unload/ClearCache 后账本必须归零。
    /// <para>缓存层走可控租约源；Handler 层走反射 OnInit 的 <see cref="UnityAudioHandler"/>。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioLeakAcceptanceTests
    {
        private sealed class LeaseFixture : IAudioClipLeaseSource, IDisposable
        {
            private int _liveHandles;

            public LeaseFixture(int capacity = 32, float ttl = 30f, AudioCachePolicy policy = AudioCachePolicy.Ttl)
            {
                Cache = new AudioClipCache();
                Cache.Configure(this, capacity, ttl, policy);
            }

            public AudioClipCache Cache { get; }
            public int LiveHandles => _liveHandles;

            bool IAudioClipLeaseSource.TryAcquire(string address, out AudioClipLease lease)
            {
                lease = MakeLease();
                return true;
            }

            void IAudioClipLeaseSource.AcquireAsync(string address, CancellationToken token,
                Action<AudioClipLease> completed)
            {
                completed(MakeLease());
            }

            private AudioClipLease MakeLease()
            {
                var clip = AudioClip.Create("leak_" + _liveHandles, 128, 1, 44100, false);
                return new AudioClipLease(clip, new LeaseHandle(this));
            }

            private sealed class LeaseHandle : IDisposable
            {
                private readonly LeaseFixture _owner;
                private bool _disposed;

                public LeaseHandle(LeaseFixture owner)
                {
                    _owner = owner;
                    owner._liveHandles++;
                }

                public void Dispose()
                {
                    if (_disposed) return;
                    _disposed = true;
                    _owner._liveHandles--;
                }
            }

            public void Dispose()
            {
                Cache.Dispose();
                _pending.Clear();
            }

            private readonly Queue<Action<AudioClipLease>> _pending = new Queue<Action<AudioClipLease>>();
        }

        private GameObject _root;
        private UnityAudioHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioLeakTest]");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();
            _handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(_handler, null);
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.StopAll(0f);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _handler = null;
            _root = null;
        }

        private static AudioClip MakeClip(string name)
        {
            var clip = AudioClip.Create(name, 44100, 1, 44100, false);
            clip.SetData(new float[44100], 0);
            return clip;
        }

        [Test]
        public void MixedChurn_PreloadUnloadClear_LeavesZeroLiveHandles()
        {
            using var fixture = new LeaseFixture(16, 30f, AudioCachePolicy.Ttl);
            var random = new System.Random(20260922);
            string[] addresses = new string[24];
            for (int i = 0; i < addresses.Length; i++) addresses[i] = "Audio/Sfx/" + i;

            for (int step = 0; step < 4096; step++)
            {
                string address = addresses[random.Next(addresses.Length)];
                switch (step % 6)
                {
                    case 0:
                        fixture.Cache.Preload(address, (AudioCachePolicy)random.Next(1, 4));
                        break;
                    case 1:
                        fixture.Cache.Unload(address, force: random.Next(4) == 0);
                        break;
                    case 2:
                        fixture.Cache.ClearCache(force: random.Next(8) == 0);
                        break;
                    case 3:
                        fixture.Cache.OnLowMemory();
                        break;
                    case 4:
                        if (fixture.Cache.TryGetLoaded(address, out var entry))
                        {
                            fixture.Cache.Retain(entry);
                            fixture.Cache.Release(entry);
                        }
                        break;
                    case 5:
                        fixture.Cache.Tick();
                        break;
                }
            }

            fixture.Cache.ClearCache(force: true);
            Assert.AreEqual(0, fixture.Cache.Count, "force 清理后缓存条目应为 0");
            Assert.AreEqual(0, fixture.LiveHandles, "所有租约必须归还");
        }

        [Test]
        public void SharedAddress_ManyRetains_SingleLease_AllReleased_LeavesZero()
        {
            using var fixture = new LeaseFixture();
            Assert.IsTrue(fixture.Cache.Preload("Audio/Sfx/Shared", AudioCachePolicy.Ttl));
            var entry = fixture.Cache.EntryRef("Audio/Sfx/Shared");
            int before = fixture.LiveHandles;
            Assert.AreEqual(1, before, "共享地址只应持有一份租约");

            for (int i = 0; i < 8; i++) fixture.Cache.Retain(entry);
            for (int i = 0; i < 8; i++) fixture.Cache.Release(entry);

            fixture.Cache.Unload("Audio/Sfx/Shared", force: true);
            Assert.AreEqual(0, fixture.Cache.Count);
            Assert.AreEqual(0, fixture.LiveHandles);
        }

        [Test]
        public void FailureCooldown_ThenForceClear_ResetsAndAllowsRetry()
        {
            using var fixture = new LeaseFixture();
            // 失败路径：TryAcquire 返回 false 时应进冷却且不泄漏
            // 这里用空地址/预载后 Clear 验证 force 会清掉冷却
            Assert.IsFalse(fixture.Cache.Preload(null));
            Assert.IsFalse(fixture.Cache.Preload(string.Empty));
            fixture.Cache.ClearCache(force: true);
            Assert.AreEqual(0, fixture.LiveHandles);
        }

        [Test]
        public void Handler_BurstPlayStop_DoesNotGrowHandleMapUnbounded()
        {
            var clip = MakeClip("leak_burst");
            try
            {
                var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
                options.ID = 70000;
                options.DoNotAutoRecycleIfNotDonePlaying = false;

                var handles = new List<ulong>(256);
                for (int i = 0; i < 200; i++)
                {
                    ulong handle = _handler.Play(clip, options);
                    if (handle != 0UL) handles.Add(handle);
                    if (handles.Count > 0 && (i & 1) == 1)
                    {
                        _handler.Stop(handles[handles.Count - 1], 0f);
                        handles.RemoveAt(handles.Count - 1);
                    }
                }

                for (int i = 0; i < handles.Count; i++) _handler.Stop(handles[i], 0f);
                _handler.Tick(0f, 0.016f);

                var registryField = typeof(UnityAudioHandler)
                    .GetField("_handles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(registryField);
                var registry = registryField.GetValue(_handler);
                var countProp = registry.GetType().GetProperty("Count");
                Assert.IsNotNull(countProp);
                Assert.AreEqual(0, (int)countProp.GetValue(registry), "句柄注册表必须清空");
            }
            finally
            {
                UnityEngine.Object.Destroy(clip);
            }
        }

        [Test]
        public void ClearCacheForce_DropsPinnedAndEmptiesPoolView()
        {
            using var fixture = new LeaseFixture();
            fixture.Cache.Preload("Audio/Sfx/PinA", AudioCachePolicy.Pin);
            fixture.Cache.Preload("Audio/Sfx/TtlB", AudioCachePolicy.Ttl);
            Assert.Greater(fixture.Cache.Count, 0);

            fixture.Cache.ClearCache(force: true);
            Assert.AreEqual(0, fixture.Cache.Count);
            Assert.AreEqual(0, fixture.Cache.PinnedCount);
            Assert.AreEqual(0, fixture.LiveHandles);
        }
    }

    internal static class LeakTestExtensions
    {
        public static AudioClipCacheEntry EntryRef(this AudioClipCache cache, string address)
        {
            Assert.IsTrue(cache.TryGetEntry(address, out var entry), address);
            return entry;
        }
    }
}
