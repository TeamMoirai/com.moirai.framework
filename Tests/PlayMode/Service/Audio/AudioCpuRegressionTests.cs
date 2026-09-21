using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// CPU 回归门禁：热路径在预算内完成。预算刻意放宽，避免 CI 抖动；
    /// 真实回归（数量级劣化）仍会被拦下。实际耗时写入测试日志。
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class AudioCpuRegressionTests
    {
        private const int Iterations = 1000;
        private const double CachedPlayStopBudgetMs = 200.0;
        private const double TickBudgetMs = 100.0;
        private const double RequestBudgetMs = 150.0;

        private GameObject _root;
        private UnityAudioHandler _handler;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioCpuTest]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            _clip = AudioClip.Create("cpu_tone", 44100 * 2, 1, 44100, false);
            _clip.SetData(new float[44100 * 2], 0);

            _handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(_handler, null);
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.StopAll(0f);
            if (_root != null) Object.Destroy(_root);
            if (_clip != null) Object.Destroy(_clip);
            _handler = null;
            _root = null;
            _clip = null;
        }

        [UnityTest]
        public IEnumerator CachedPlayStop_StaysWithinBudget()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过 CPU 回归");
                yield break;
            }

            var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
            options.DoNotAutoRecycleIfNotDonePlaying = false;

            for (int i = 0; i < 32; i++)
            {
                ulong h = _handler.Play(_clip, options);
                if (h != 0UL) _handler.Stop(h, 0f);
            }

            yield return null;

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                ulong handle = _handler.Play(_clip, options);
                if (handle != 0UL) _handler.Stop(handle, 0f);
            }

            clock.Stop();
            TestContext.WriteLine($"CPU,cached-play-stop,{Iterations},{clock.Elapsed.TotalMilliseconds:F3}ms");
            Assert.Less(clock.Elapsed.TotalMilliseconds, CachedPlayStopBudgetMs,
                $"缓存命中 Play/Stop {Iterations} 次应低于 {CachedPlayStopBudgetMs}ms");
        }

        [UnityTest]
        public IEnumerator IdleTick_StaysWithinBudget()
        {
            yield return null;

            for (int i = 0; i < 32; i++) _handler.Tick(0.016f, 0.016f);

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                _handler.Tick(0.016f, 0.016f);
            }

            clock.Stop();
            TestContext.WriteLine($"CPU,idle-tick,{Iterations},{clock.Elapsed.TotalMilliseconds:F3}ms");
            Assert.Less(clock.Elapsed.TotalMilliseconds, TickBudgetMs,
                $"空闲 Tick {Iterations} 次应低于 {TickBudgetMs}ms");
        }

        [UnityTest]
        public IEnumerator HotRequestPath_StaysWithinBudget()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过 CPU 回归");
                yield break;
            }

            var request = new AudioPlayRequest(8001, 1f, 1f, EAudioTrack.Sfx, 128, AudioPlayFlags.DoNotAutoRecycle);

            for (int i = 0; i < 32; i++)
            {
                ulong h = _handler.Play(_clip, request, null);
                if (h != 0UL) _handler.Stop(h, 0f);
            }

            yield return null;

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                ulong handle = _handler.Play(_clip, request, null);
                if (handle != 0UL) _handler.Stop(handle, 0f);
            }

            clock.Stop();
            TestContext.WriteLine($"CPU,hot-request,{Iterations},{clock.Elapsed.TotalMilliseconds:F3}ms");
            Assert.Less(clock.Elapsed.TotalMilliseconds, RequestBudgetMs,
                $"16B 热请求 Play/Stop {Iterations} 次应低于 {RequestBudgetMs}ms");
        }

        [UnityTest]
        public IEnumerator PriorityStealSelection_StaysWithinBudget()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过 CPU 回归");
                yield break;
            }

            var hold = new List<ulong>(32);
            var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
            options.DoNotAutoRecycleIfNotDonePlaying = true;
            options.Loop = true;
            options.Persistent = true;

            for (int i = 0; i < 32; i++)
            {
                options.ID = 9000 + i;
                options.Priority = 200;
                ulong h = _handler.Play(_clip, options);
                if (h != 0UL) hold.Add(h);
            }

            yield return null;

            var steal = AudioPlayOptions.Create(EAudioTrack.Sfx);
            steal.DoNotAutoRecycleIfNotDonePlaying = false;

            var clock = Stopwatch.StartNew();
            for (int i = 0; i < Iterations; i++)
            {
                steal.Priority = 1 + (i % 255);
                ulong h = _handler.Play(_clip, steal);
                if (h != 0UL) _handler.Stop(h, 0f);
            }

            clock.Stop();
            TestContext.WriteLine($"CPU,priority-steal,{Iterations},{clock.Elapsed.TotalMilliseconds:F3}ms");
            Assert.Less(clock.Elapsed.TotalMilliseconds, CachedPlayStopBudgetMs,
                $"优先级抢占选择 {Iterations} 次应低于 {CachedPlayStopBudgetMs}ms");

            for (int i = 0; i < hold.Count; i++) _handler.Stop(hold[i], 0f);
        }
    }
}
