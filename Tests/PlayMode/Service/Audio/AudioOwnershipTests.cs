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
    /// 所有权 / 世代 / 句柄身份验收。
    /// <para>缓存层复用 <c>IAudioClipLeaseSource</c> 假件语义（与 EditorMode 的 AudioCacheTestSupport 同构——
    /// PlayMode 程序集看不见 EditorMode 内部类型，故在此提供等价夹具）。</para>
    /// <para>句柄层走 <see cref="AudioHandleRegistry{TVoice}"/> 与 <see cref="UnityAudioHandler"/> 重绑路径。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioOwnershipTests
    {
        private const string A = "Audio/Sfx/Confirm";
        private const string B = "Audio/Sfx/LevelUp";

        #region 租约夹具 [LEASE FIXTURE]

        /// <summary>
        /// 可控租约源：记账「同地址后端加载次数」与「未归还租约数」，异步完成由测试手动放行。
        /// </summary>
        private sealed class LeaseFixture : IAudioClipLeaseSource, IDisposable
        {
            private readonly Dictionary<string, int> _loads = new Dictionary<string, int>();
            private readonly Queue<Action<AudioClipLease>> _pending = new Queue<Action<AudioClipLease>>();
            private int _liveHandles;

            public LeaseFixture(int capacity = 16, float ttl = 30f, AudioCachePolicy policy = AudioCachePolicy.Ttl)
            {
                Cache = new AudioClipCache();
                Cache.Configure(this, capacity, ttl, policy);
            }

            public AudioClipCache Cache { get; }
            public int LiveHandles => _liveHandles;
            public int PendingCount => _pending.Count;
            public bool ManualAsync { get; set; }
            public bool FailLoads { get; set; }

            public int LoadCount(string address) => _loads.TryGetValue(address, out var n) ? n : 0;

            bool IAudioClipLeaseSource.TryAcquire(string address, out AudioClipLease lease)
            {
                Bump(address);
                lease = FailLoads ? default : MakeLease();
                return !FailLoads;
            }

            void IAudioClipLeaseSource.AcquireAsync(string address, CancellationToken token,
                Action<AudioClipLease> completed)
            {
                Bump(address);
                if (ManualAsync)
                {
                    _pending.Enqueue(completed);
                    return;
                }

                completed(FailLoads ? default : MakeLease());
            }

            public void CompleteNext(bool fail = false)
            {
                if (_pending.Count == 0) throw new InvalidOperationException("没有待放行的异步加载");
                _pending.Dequeue()(fail ? default : MakeLease());
            }

            public AudioClipCacheEntry Entry(string address)
            {
                Assert.IsTrue(Cache.TryGetEntry(address, out var entry), $"缓存中应有 {address}");
                return entry;
            }

            public void Dispose()
            {
                Cache.Dispose();
                _pending.Clear();
            }

            private void Bump(string address)
            {
                _loads.TryGetValue(address, out var n);
                _loads[address] = n + 1;
            }

            private AudioClipLease MakeLease()
            {
                var clip = AudioClip.Create("lease-clip", 1, 1, 44100, false);
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
        }

        #endregion 租约夹具 [LEASE FIXTURE]

        private sealed class TestVoice : IAudioVoiceRef
        {
            public int UserId { get; set; }
            public ulong BoundHandle { get; set; }
        }

        /// <summary>
        /// 同地址两次「播放」共用一条后端租约：加载 1 次、LiveHandles=1、RefCount=2。
        /// </summary>
        [Test]
        public void SharedAddress_TwoPlays_ShareOneLease_RefCountTracksConsumers()
        {
            using var f = new LeaseFixture();
            Assert.IsTrue(f.Cache.Preload(A, AudioCachePolicy.Ttl), "预加载应成功");

            var entry = f.Entry(A);
            Assert.AreEqual(1, f.LoadCount(A), "同地址只应向后端取一次租约");
            Assert.AreEqual(1, f.LiveHandles, "共享地址只持有一条后端租约");
            Assert.AreEqual(0, entry.RefCount, "预加载本身不构成声部引用");

            // 两次播放 = 两次 Retain（对应 AudioAgent.OnClipReady）
            f.Cache.Retain(entry);
            f.Cache.Retain(entry);

            Assert.AreEqual(2, entry.RefCount, "两次播放应各有独立引用");
            Assert.AreEqual(1, f.LiveHandles, "引用增加不得放大后端租约");
            Assert.AreEqual(1, f.LoadCount(A), "命中缓存不得二次加载");

            f.Cache.Release(entry);
            Assert.AreEqual(1, entry.RefCount, "第一次 Stop 只减一个消费者");
            Assert.AreEqual(1, f.LiveHandles, "仍有消费者时租约不得归还");

            f.Cache.Release(entry);
            Assert.AreEqual(0, entry.RefCount, "全部 Stop 后引用应归零");
            Assert.AreEqual(1, f.LiveHandles, "Ttl 策略归零后留池，租约仍由缓存持有");
            Assert.IsTrue(entry.IsLoaded);
        }

        /// <summary>
        /// 迟到的加载完成不得写入已被复用的条目：版本/身份校验必须作废旧结果并就地归还租约。
        /// </summary>
        [Test]
        public void LateCompletion_AfterEntryRecycled_CannotWriteIntoReusedEntry()
        {
            var first = new LeaseFixture(capacity: 4);
            first.ManualAsync = true;
            first.Cache.PreloadAsync(A, AudioCachePolicy.Ttl, null);
            Assert.AreEqual(1, first.PendingCount);

            // 关停把条目还给全局池；下一缓存会复用同一实例
            first.Dispose();

            var second = new LeaseFixture(capacity: 4);
            Assert.IsTrue(second.Cache.Preload(A, AudioCachePolicy.Ttl));
            int entriesBefore = second.Cache.Count;
            int liveBefore = second.LiveHandles;
            var expected = second.Entry(A).Clip;

            // 迟到回调仍持有旧世代的 entry 引用
            first.CompleteNext();

            Assert.AreEqual(0, first.LiveHandles, "迟到回调自带的租约必须当场归还");
            Assert.AreEqual(entriesBefore, second.Cache.Count, "迟到回调不得改写另一个缓存的条目数");
            Assert.AreEqual(liveBefore, second.LiveHandles, "新主人的租约不该被动过");
            Assert.IsTrue(second.Entry(A).IsLoaded);
            Assert.AreSame(expected, second.Entry(A).Clip, "新主人的 clip 不得被迟到结果覆盖");

            second.Dispose();
        }

        /// <summary>
        /// 关停后迟到完成：条目已 Clear 回池，版本/身份校验必须作废结果并就地归还租约，不得复活条目。
        /// </summary>
        [Test]
        public void LateCompletion_AfterShutdown_DoesNotResurrectEntry()
        {
            var f = new LeaseFixture(capacity: 2);
            f.ManualAsync = true;
            f.Cache.PreloadAsync(A, AudioCachePolicy.Ttl, null);
            Assert.AreEqual(1, f.PendingCount);
            Assert.AreEqual(1, f.Cache.Count);

            f.Dispose();
            Assert.AreEqual(0, f.Cache.Count, "关停应清空缓存");

            // 迟到回调仍会带着租约进来
            f.CompleteNext();

            Assert.AreEqual(0, f.LiveHandles, "作废完成的租约必须归还");
            Assert.AreEqual(0, f.Cache.Count, "迟到完成不得复活已关停的条目");
        }

        /// <summary>
        /// Stop/End 归还缓存引用后，Ttl 条目 RefCount 必须回到 0（留池等待 TTL，而非悬挂引用）。
        /// </summary>
        [Test]
        public void StopEnd_ReleasesCacheRetain_TtlEntryRefCountReturnsToZero()
        {
            using var f = new LeaseFixture();
            Assert.IsTrue(f.Cache.Preload(A, AudioCachePolicy.Ttl));
            var entry = f.Entry(A);

            // 模拟播放：OnClipReady 路径的 Retain
            f.Cache.Retain(entry);
            f.Cache.Retain(entry);
            Assert.AreEqual(2, entry.RefCount);
            Assert.IsFalse(entry.InLru, "引用中的条目不应挂在 LRU 上");

            // 模拟 Stop/End：ReleaseClipCacheEntry 路径
            f.Cache.Release(entry);
            f.Cache.Release(entry);

            Assert.AreEqual(0, entry.RefCount, "全部 Stop/End 后 Ttl 条目引用必须归零");
            Assert.IsTrue(entry.InLru, "归零后应回 LRU 等待到期/驱逐");
            Assert.AreEqual(1, f.LiveHandles, "Ttl 留池期间租约仍由缓存持有");
            Assert.AreEqual(0, f.Cache.LoadingCount);
        }

        /// <summary>
        /// None 策略：最后一次 Release 当场摘条目并归还租约，不留悬挂引用。
        /// </summary>
        [Test]
        public void StopEnd_NonePolicy_DropsEntryAndReturnsLease()
        {
            using var f = new LeaseFixture(policy: AudioCachePolicy.None);
            Assert.IsTrue(f.Cache.Preload(A, AudioCachePolicy.None));
            // None + 无引用：加载完成即回收（预加载不构成引用）
            Assert.AreEqual(0, f.Cache.Count);
            Assert.AreEqual(0, f.LiveHandles);

            f.Cache.Preload(A, AudioCachePolicy.Ttl);
            var entry = f.Entry(A);
            f.Cache.Retain(entry);
            Assert.AreEqual(1, entry.RefCount);
            f.Cache.Release(entry);

            // Ttl 归零留池；再 Unload 才还租约
            Assert.AreEqual(0, entry.RefCount);
            Assert.IsTrue(f.Cache.Unload(A, force: true));
            Assert.AreEqual(0, f.Cache.Count);
            Assert.AreEqual(0, f.LiveHandles);
        }

        /// <summary>
        /// 句柄重绑后旧身份不得继续解析：旧句柄从注册表摘除、声部侧句柄指向新值。
        /// </summary>
        [Test]
        public void Handle_AfterRebind_IsNotTheOldIdentity()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 7 };

            ulong oldHandle = registry.NextHandle();
            registry.Bind(oldHandle, voice);
            registry.RegisterUser(7, oldHandle);

            ulong newHandle = registry.NextHandle();
            Assert.AreNotEqual(oldHandle, newHandle, "生成器必须吐出不同句柄");

            registry.Bind(newHandle, voice);
            registry.RegisterUser(7, newHandle);

            Assert.IsFalse(registry.IsRegistered(oldHandle), "旧句柄必须已卸绑");
            Assert.IsFalse(registry.TryGet(oldHandle, out _), "旧身份不得再解析到声部");
            Assert.IsTrue(registry.TryGet(newHandle, out var mapped));
            Assert.AreSame(voice, mapped);
            Assert.AreEqual(newHandle, voice.BoundHandle, "声部侧句柄应指向新身份");
            Assert.AreEqual(1, registry.Count, "重绑不得留下双句柄");

            // 旧句柄上的 Stop 语义：查不到映射 = 已停止，重复 Release 返回 false
            Assert.IsFalse(registry.Release(oldHandle, out _), "重绑后旧句柄应已不在册");
            Assert.IsTrue(registry.IsRegistered(newHandle), "误用旧句柄不得摘掉新绑定");
            Assert.IsTrue(registry.Release(newHandle, out var released));
            Assert.AreSame(voice, released);
            Assert.AreEqual(0UL, voice.BoundHandle);
        }

        /// <summary>
        /// Handler 层：同 ID 换播后旧句柄不是新身份，GetAgentByHandle(old) 必须落空。
        /// </summary>
        [UnityTest]
        public System.Collections.IEnumerator UnityHandler_ReplaySameId_OldHandleIsStale()
        {
            if (!Application.isPlaying) Assert.Ignore("需要 PlayMode");

            var root = new GameObject("[AudioOwnership]");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<AudioListener>();
            var clip = AudioClip.Create("own_tone", 44100, 1, 44100, false);
            clip.SetData(new float[44100], 0);

            var handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(handler, null);

            if (handler.AudioCategories == null || handler.AudioCategories.Length == 0)
            {
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(clip);
                Assert.Ignore("AudioGroupConfigs 未配置，跳过");
                yield break;
            }

            var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
            options.ID = 7001;
            options.DoNotAutoRecycleIfNotDonePlaying = true;

            ulong first = handler.Play(clip, options);
            Assert.AreNotEqual(0UL, first);

            handler.StopByID(7001, 0f);
            yield return null;
            handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);

            ulong second = handler.Play(clip, options);
            Assert.AreNotEqual(0UL, second);
            Assert.AreNotEqual(first, second, "重播必须拿到新句柄身份");
            Assert.IsNull(handler.GetAgentByHandle(first), "旧句柄不得解析到新声部");
            Assert.IsNotNull(handler.GetAgentByHandle(second));

            handler.StopAll(0f);
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(clip);
        }
    }
}
