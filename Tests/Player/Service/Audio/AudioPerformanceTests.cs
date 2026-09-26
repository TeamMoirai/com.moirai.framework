using System;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 热路径 0-GC 验收：稳态 Play / 音量 / IsPlaying / Stop、空闲 Tick、按 ID 遍历均不得分配。
    /// <para><b>玩家专用</b>：托管分配计数器在 Unity 编辑器 Mono 下不推进（本机实测 64MB 分配仍报 0），
    /// 零分配断言在编辑器里会无条件成立，故本夹具住在 <c>Moirai.Atropos.Tests.Player</c>
    /// （<c>UNITY_INCLUDE_TESTS</c> + <c>!UNITY_EDITOR</c>），只随玩家构建的测试运行执行；
    /// 编辑器套件里不再出现（既不假绿也不假跳）。</para>
    /// <para>自建隔离：反射 OnInit <see cref="UnityAudioHandler"/>，不依赖 GameEntry。</para>
    /// <para>测量口径见 <see cref="AllocationCapture.MeasureManaged"/>（预热一次丢弃后计数）；
    /// 计数器仍不可用的运行时（个别 IL2CPP 配置）整组按 Ignore 收口——"测不出分配"不等于"没有分配"。</para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class AudioPerformanceTests
    {
        private GameObject _root;
        private UnityAudioHandler _handler;
        private AudioClip _clip;
        private AudioPlayRequest _request;
        private AudioPlayOptions _options;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioPerfTest]");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            _clip = AudioClip.Create("perf_tone", 44100 * 2, 1, 44100, false);
            _clip.SetData(new float[44100 * 2], 0);

            _handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(init, "UnityAudioHandler.OnInit 应存在");
            init.Invoke(_handler, null);

            _request = new AudioPlayRequest(9001, 1f, 1f, EAudioTrack.Sfx, 128, EAudioPlayFlags.DoNotAutoRecycle);
            _options = AudioPlayOptions.Create(EAudioTrack.Sfx);
            _options.ID = 9001;
            _options.DoNotAutoRecycleIfNotDonePlaying = true;
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.StopAll(0f);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_clip != null) UnityEngine.Object.Destroy(_clip);
            _handler = null;
            _root = null;
            _clip = null;
        }

        [Test]
        public void MeasureManaged_DetectsKnownAllocation()
        {
            AllocationCapture.CalibrateKnownAllocation();
        }

        [UnityTest]
        public System.Collections.IEnumerator CachedPlayVolumeIsPlayingStop_AllocatesZeroBytes()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置");
                yield break;
            }

            // 预热：建立通道、绑定句柄路径
            for (int i = 0; i < 16; i++)
            {
                ulong h = _handler.Play(_clip, _options);
                if (h != 0UL)
                {
                    _handler.GetAgentByHandle(h);
                    _handler.IsPlaying(h);
                    _handler.Stop(h, 0f);
                }
            }

            yield return null;

            long bytes = AllocationCapture.MeasureManaged("cached-play-stop", 200, () =>
            {
                ulong handle = _handler.Play(_clip, _options);
                if (handle == 0UL) return;
                _handler.IsPlaying(handle);
                _handler.Stop(handle, 0f);
            }, b => Assert.AreEqual(0L, b, "稳态 Play/IsPlaying/Stop 不得分配"));

            Assert.AreEqual(0L, bytes);
        }

        [UnityTest]
        public System.Collections.IEnumerator CachedPlayViaOptions_AllocatesZeroBytes()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置");
                yield break;
            }

            for (int i = 0; i < 16; i++)
            {
                ulong h = _handler.Play(_clip, _options);
                if (h != 0UL) _handler.Stop(h, 0f);
            }

            yield return null;

            AllocationCapture.MeasureManaged("cached-play-options", 200, () =>
            {
                ulong handle = _handler.Play(_clip, _options);
                if (handle != 0UL) _handler.Stop(handle, 0f);
            }, b => Assert.AreEqual(0L, b, "Options 路径稳态不得分配"));
        }

        [UnityTest]
        public System.Collections.IEnumerator IdleHandlerTick_AllocatesZeroBytes()
        {
            yield return null;
            for (int i = 0; i < 8; i++) _handler.Tick(0.016f, 0.016f);

            AllocationCapture.MeasureManaged("idle-tick", 500,
                () => _handler.Tick(0.016f, 0.016f),
                b => Assert.AreEqual(0L, b, "空闲 Tick 不得分配"));
        }

        [UnityTest]
        public System.Collections.IEnumerator ForEachHandleByID_EmptyOrNoMatch_AllocatesZeroBytes()
        {
            yield return null;
            AllocationCapture.MeasureManaged("foreach-empty", 500,
                () => _handler.ForEachHandleByID(424242, _ => { }),
                b => Assert.AreEqual(0L, b, "空匹配 ForEach 不得分配"));
        }

        [UnityTest]
        public System.Collections.IEnumerator ForEachHandleByID_Hit_AllocatesZeroBytes()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置");
                yield break;
            }

            var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
            options.ID = 777;
            options.DoNotAutoRecycleIfNotDonePlaying = true;
            options.Loop = true;
            options.Persistent = true;

            ulong handle = _handler.Play(_clip, options);
            if (handle == 0UL)
            {
                Assert.Ignore("无可用声部");
                yield break;
            }

            yield return null;

            int hits = 0;
            AllocationCapture.MeasureManaged("foreach-hit", 500,
                () =>
                {
                    hits = 0;
                    _handler.ForEachHandleByID(777, _ => hits++);
                },
                b =>
                {
                    Assert.AreEqual(0L, b, "命中 ForEach 不得分配");
                    Assert.AreEqual(1, hits);
                });

            _handler.Stop(handle, 0f);
        }
    }
}
