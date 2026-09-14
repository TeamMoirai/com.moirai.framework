using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// AudioHandleRegistry 单元测试：句柄绑定、用户 ID 映射、BoundHandle 单点维护。
    /// </summary>
    public sealed class AudioHandleRegistryTests
    {
        private sealed class TestVoice : IAudioVoiceRef
        {
            public int UserId { get; set; }
            public ulong BoundHandle { get; set; }
        }

        [Test]
        public void Bind_WritesMapAndVoiceBoundHandle()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 7 };

            ulong handle = registry.NextHandle();
            registry.Bind(handle, voice);

            Assert.IsTrue(registry.TryGet(handle, out var mapped));
            Assert.AreSame(voice, mapped);
            Assert.AreEqual(handle, voice.BoundHandle, "Bind 必须同步声部侧句柄");
            Assert.IsTrue(registry.IsRegistered(handle));
        }

        [Test]
        public void Release_ClearsMapAndBoundHandle()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 7 };
            ulong handle = registry.NextHandle();
            registry.Bind(handle, voice);
            registry.RegisterUser(7, handle);

            Assert.IsTrue(registry.Release(handle, out var released));
            Assert.AreSame(voice, released);
            Assert.IsFalse(registry.IsRegistered(handle));
            Assert.AreEqual(0UL, voice.BoundHandle, "Release 必须清零声部侧句柄");
        }

        [Test]
        public void Bind_RebindWithoutExplicitRelease_ReleasesOldHandle()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 7 };

            ulong oldHandle = registry.NextHandle();
            registry.Bind(oldHandle, voice);
            registry.RegisterUser(7, oldHandle);

            ulong newHandle = registry.NextHandle();
            registry.Bind(newHandle, voice);
            registry.RegisterUser(7, newHandle);

            Assert.IsFalse(registry.IsRegistered(oldHandle), "重绑前旧句柄必须被卸绑");
            Assert.IsTrue(registry.IsRegistered(newHandle));
            Assert.AreEqual(newHandle, voice.BoundHandle);
            Assert.AreEqual(1, registry.Count, "同一声部不得出现双句柄");
        }

        [Test]
        public void Bind_SameHandleOtherVoice_ReplacesPrevious()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var first = new TestVoice { UserId = 1 };
            var second = new TestVoice { UserId = 2 };
            ulong handle = registry.NextHandle();

            registry.Bind(handle, first);
            registry.RegisterUser(1, handle);
            registry.Bind(handle, second);
            registry.RegisterUser(2, handle);

            Assert.AreEqual(0UL, first.BoundHandle, "被替换的声部侧句柄应清零");
            Assert.AreEqual(handle, second.BoundHandle);
            Assert.IsTrue(registry.TryGet(handle, out var mapped));
            Assert.AreSame(second, mapped);
        }

        [Test]
        public void ForEachHandleByUser_SurvivesReleaseDuringIteration()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            var voice = new TestVoice { UserId = 42 };
            ulong h1 = registry.NextHandle();
            ulong h2 = registry.NextHandle();
            ulong h3 = registry.NextHandle();
            registry.Bind(h1, voice);
            registry.Bind(h2, new TestVoice { UserId = 42 });
            registry.Bind(h3, new TestVoice { UserId = 42 });
            registry.RegisterUser(42, h1);
            registry.RegisterUser(42, h2);
            registry.RegisterUser(42, h3);

            int visited = 0;
            registry.ForEachHandleByUser(42, handle =>
            {
                visited++;
                registry.Release(handle, out _);
            });

            Assert.AreEqual(3, visited, "快照迭代不得因 Release 跳过元素");
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void NextHandle_SkipsZeroAndIncrements()
        {
            var registry = new AudioHandleRegistry<TestVoice>();
            Assert.AreEqual(1UL, registry.NextHandle());
            Assert.AreEqual(2UL, registry.NextHandle());
        }
    }
}
