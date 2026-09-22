using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 中间件事件路径面：映射表优先、按名回落只提示一次、计数与播放同一条解析路径、播放失败可归因。
    /// <para>对应上线门槛 G1（<c>GetEventPathFromClip</c> 与映射表联调）与 G2-1（事件映射策略）、G5（失败注入）。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioMiddlewareEventMapTests
    {
        /// <summary>记录播放路径与推导调用的假桥。</summary>
        private sealed class RecordingBridge : IAudioMiddlewareBridge
        {
            /// <summary>返回 0 时按事件路径置真——模拟「路径写错／库没加载」。</summary>
            public readonly HashSet<string> FailingPaths = new HashSet<string>(StringComparer.Ordinal);

            public readonly List<string> PlayedPaths = new List<string>();
            public int DeriveCalls;

            public bool Initialize(Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }

            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D)
            {
                PlayedPaths.Add(eventPath);
                return FailingPaths.Contains(eventPath) ? 0UL : 1000UL + (ulong)PlayedPaths.Count;
            }

            public void StopInstance(ulong instanceId, bool immediate) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }
            public void SetBusVolume(string busPath, float volume) { }
            public float GetBusVolume(string busPath) => 1f;
            public bool IsPlaying(ulong instanceId) => true;

            public string GetEventPathFromClip(AudioClip clip)
            {
                DeriveCalls++;
                return clip == null ? null : "event:/Derived/" + clip.name;
            }
        }

        private RecordingBridge _bridge;
        private FmodAudioHandler _handler;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            AudioWarnOnce.Reset();
            _bridge = new RecordingBridge();
            _handler = new FmodAudioHandler();
            _handler.SetBridge(_bridge);
            _clip = AudioClip.Create("BossIdle", 1, 1, 44100, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_clip != null) UnityEngine.Object.DestroyImmediate(_clip);
            AudioWarnOnce.Reset();
        }

        private ulong PlayClip()
            => _handler.Play(_clip, AudioPlayOptions.Create(EAudioTrack.Sfx));

        private void MapClipTo(string eventPath)
        {
            _handler.SetEventMappings(new[]
            {
                new AudioEventMapping { Clip = _clip, EventPath = eventPath },
            });
        }

        [Test]
        public void Play_MappedClip_UsesEventPathAndNeverDerives()
        {
            MapClipTo("event:/Authoring/Boss/Idle");

            Assert.AreNotEqual(0UL, PlayClip());
            Assert.AreEqual("event:/Authoring/Boss/Idle", _bridge.PlayedPaths[0]);
            Assert.AreEqual(0, _bridge.DeriveCalls, "命中映射就不该再按 clip.name 推导");
        }

        [Test]
        public void Play_UnmappedClip_DerivesAndWarnsOnce()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("事件路径由 clip.name 推导"));

            Assert.AreNotEqual(0UL, PlayClip());
            Assert.AreEqual("event:/Derived/BossIdle", _bridge.PlayedPaths[0]);

            // 第二次不再有 Expect：再落一条 Warning 会以「意外日志」判负
            Assert.AreNotEqual(0UL, PlayClip());
            Assert.AreEqual(2, _bridge.PlayedPaths.Count);
        }

        [Test]
        public void CurrentlyPlayingCount_MappedClip_MatchesWhatWasPlayed()
        {
            MapClipTo("event:/Authoring/Boss/Idle");

            Assert.AreNotEqual(0UL, PlayClip());
            Assert.AreEqual(1, _handler.CurrentlyPlayingCount(_clip),
                "计数必须与播放同一条解析路径，否则命中映射的 clip 恒查到 0");
        }

        [Test]
        public void SetEventMappings_AfterPlay_ResolvesAgainstTheNewTable()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("事件路径由 clip.name 推导"));
            Assert.AreNotEqual(0UL, PlayClip());

            MapClipTo("event:/Authoring/Boss/Idle");
            Assert.AreNotEqual(0UL, PlayClip());

            Assert.AreEqual("event:/Derived/BossIdle", _bridge.PlayedPaths[0]);
            Assert.AreEqual("event:/Authoring/Boss/Idle", _bridge.PlayedPaths[1],
                "换表要立刻作废旧索引，否则运行期改配置表现为「改了没效果」");
        }

        [Test]
        public void Play_EventPathFailed_ReturnsZeroAndWarnsOnce()
        {
            _bridge.FailingPaths.Add("event:/Sfx/Hit");

            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("事件 event:/Sfx/Hit 播放失败"));

            var request = new AudioPlayRequest(7, 1f, 1f, EAudioTrack.Sfx, 128, AudioPlayFlags.None);
            Assert.AreEqual(0UL, _handler.Play("event:/Sfx/Hit", request, null));
            Assert.AreEqual(0UL, _handler.Play("event:/Sfx/Hit", request, null),
                "失败仍是 0 句柄，不抛不占位");
            Assert.AreEqual(2, _bridge.PlayedPaths.Count, "去重只去告警，不能去播放请求");
        }
    }
}
