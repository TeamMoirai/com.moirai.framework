using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// AudioHandleRegistry 单元测试：句柄绑定、代次防伪、用户 ID 链、BoundHandle 单点维护。
    /// <para>句柄是打包值（高位代次 + 低位槽号），所以这里钉的不是"等于 1、2、3"这种实现细节，
    /// 而是调用方真正依赖的四条：非零、不复用、槽位复用后旧句柄必须判假、以及遍历/解绑互不破坏。</para>
    /// </summary>
    public sealed class AudioHandleRegistryTests
    {
        private sealed class TestVoice : IAudioVoiceRef
        {
            public int UserId { get; set; }
            public ulong BoundHandle { get; set; }
            public int VoiceSlot { get; set; } = -1;
        }

        [Test]
        public void Bind_WritesMapAndVoiceSideHandles()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 7 };

            ulong handle = registry.Bind(voice);

            Assert.AreNotEqual(0UL, handle);
            Assert.IsTrue(registry.TryGet(handle, out var mapped));
            Assert.AreSame(voice, mapped);
            Assert.AreEqual(handle, voice.BoundHandle, "Bind 必须同步声部侧句柄");
            Assert.Greater(voice.VoiceSlot, -1, "Bind 必须写上槽位");
            Assert.IsTrue(registry.IsRegistered(handle));
        }

        [Test]
        public void Release_ClearsHandlesAndSlot()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 7 };
            ulong handle = registry.Bind(voice);
            registry.RegisterUser(handle);

            Assert.IsTrue(registry.Release(handle, out var released));
            Assert.AreSame(voice, released);
            Assert.IsFalse(registry.IsRegistered(handle));
            Assert.AreEqual(0UL, voice.BoundHandle, "Release 必须清零声部侧句柄");
            Assert.AreEqual(-1, voice.VoiceSlot, "Release 必须摘掉槽位，否则声部复用时带着旧下标");
        }

        [Test]
        public void Bind_RebindWithoutExplicitRelease_ReleasesOldHandle()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 7 };

            ulong oldHandle = registry.Bind(voice);
            registry.RegisterUser(oldHandle);

            ulong newHandle = registry.Bind(voice);
            registry.RegisterUser(newHandle);

            Assert.IsFalse(registry.IsRegistered(oldHandle), "重绑前旧句柄必须被卸绑");
            Assert.IsTrue(registry.IsRegistered(newHandle));
            Assert.AreEqual(newHandle, voice.BoundHandle);
            Assert.AreEqual(1, registry.Count, "同一声部不得出现双句柄");
        }

        [Test]
        public void Bind_SameSlotAfterRelease_CarriesNewGeneration()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var first = new TestVoice { UserId = 1 };
            ulong stale = registry.Bind(first);
            Assert.IsTrue(registry.Release(stale, out _));

            var second = new TestVoice { UserId = 2 };
            ulong fresh = registry.Bind(second);

            Assert.AreNotEqual(stale, fresh, "槽位可以复用，句柄不可以——代次必须不同");
            Assert.IsFalse(registry.TryGet(stale, out _), "陈旧句柄不得命中复用后的声部");
            Assert.IsTrue(registry.TryGet(fresh, out var resolved));
            Assert.AreSame(second, resolved);
        }

        [Test]
        public void TryGet_GarbageHandles_DoNotThrow()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            registry.Bind(new TestVoice { UserId = 1 });

            // 0、越界槽号、以及"槽号碰巧落在范围内但代次不符"都必须安静判假
            Assert.IsFalse(registry.TryGet(0UL, out _));
            Assert.IsFalse(registry.TryGet(1UL << 40, out _));
            Assert.IsFalse(registry.TryGet(0xDEADBEEFUL, out _));
        }

        [Test]
        public void RegisterUser_Duplicate_IsNotLinkedTwice()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 42 };
            ulong handle = registry.Bind(voice);

            Assert.IsTrue(registry.RegisterUser(handle));
            Assert.IsFalse(registry.RegisterUser(handle), "重复登记不得再次入链——同 ID 链会自环");

            int visited = 0;
            registry.ForEachHandleByUser(42, _ => visited++);
            Assert.AreEqual(1, visited);
        }

        [Test]
        public void ForEachHandleByUser_SurvivesReleaseDuringIteration()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            ulong h1 = registry.Bind(new TestVoice { UserId = 42 });
            ulong h2 = registry.Bind(new TestVoice { UserId = 42 });
            ulong h3 = registry.Bind(new TestVoice { UserId = 42 });
            registry.RegisterUser(h1);
            registry.RegisterUser(h2);
            registry.RegisterUser(h3);

            int visited = 0;
            registry.ForEachHandleByUser(42, handle =>
            {
                visited++;
                registry.Release(handle, out _);
            });

            Assert.AreEqual(3, visited, "先摘 next 再回调，不得因 Release 跳过元素");
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void ForEachHandleByUser_IgnoresOtherUsersInSameBucket()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            ulong mine = registry.Bind(new TestVoice { UserId = 100 });
            ulong other = registry.Bind(new TestVoice { UserId = 101 });
            registry.RegisterUser(mine);
            registry.RegisterUser(other);

            int visited = 0;
            registry.ForEachHandleByUser(100, _ => visited++);
            Assert.AreEqual(1, visited, "同桶不同 ID 不得被串进来");
        }

        [Test]
        public void Slots_EnumerateLiveVoicesOnly()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            ulong keep = registry.Bind(new TestVoice { UserId = 1 });
            ulong drop = registry.Bind(new TestVoice { UserId = 2 });
            registry.RegisterUser(keep);
            registry.RegisterUser(drop);
            registry.Release(drop, out _);

            int seen = 0;
            bool sawDropped = false;
            foreach (var slot in registry.Slots)
            {
                seen++;
                if (slot.Handle == drop) sawDropped = true;
                Assert.AreSame(registry, registry);   // 读一次 Current 的形态，确保不是空转
                Assert.IsNotNull(slot.Voice);
            }

            Assert.AreEqual(1, seen);
            Assert.IsFalse(sawDropped, "已释放的槽位不得再被枚举出来");
        }

        [Test]
        public void Clear_ResetsVoiceSideState()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 5 };
            ulong handle = registry.Bind(voice);
            registry.RegisterUser(handle);

            registry.Clear();

            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(0UL, voice.BoundHandle, "Clear 也要抹声部侧句柄，否则重绑路径会把它当已注册");
            Assert.AreEqual(-1, voice.VoiceSlot);
            Assert.IsFalse(registry.TryGet(handle, out _));
        }

        [Test]
        public void Bind_GrowsPastInitialCapacity_WithoutLosingLiveHandles()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var handles = new System.Collections.Generic.List<ulong>();
            var voices = new System.Collections.Generic.List<TestVoice>();

            // 越过初始容量与桶表规模，逼一次扩容；扩完之后旧句柄必须还能逐个查到原声部
            for (int i = 0; i < 70; i++)
            {
                var voice = new TestVoice { UserId = i % 7 };
                ulong handle = registry.Bind(voice);
                Assert.AreNotEqual(0UL, handle);
                registry.RegisterUser(handle);
                handles.Add(handle);
                voices.Add(voice);
            }

            Assert.AreEqual(70, registry.Count);
            for (int i = 0; i < handles.Count; i++)
            {
                Assert.IsTrue(registry.TryGet(handles[i], out var found), $"第 {i} 条句柄扩容后应仍可解析");
                Assert.AreSame(voices[i], found);
            }

            int perUser0 = 0;
            registry.ForEachHandleByUser(0, _ => perUser0++);
            Assert.AreEqual(10, perUser0, "扩容后同 ID 链必须重挂完整");
        }
    }
}
