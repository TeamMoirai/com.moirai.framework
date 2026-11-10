using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 中间件音高契约：音轨倍率乘入起播、实时推播到在播声部与单声部基础音高。
    /// </summary>
    /// <remarks>
    /// 本文件只留「与具体 SDK 无关」的框架契约面，夹具用本地的 <see cref="RecordingPitchBridge"/>； <br />
    /// FMOD / Wwise 两个插件装配（<c>com.moirai.framework.plugin</c> 的 <c>fmod/</c>、<c>wwise/</c>）的桥自带语义， <br />
    /// 它们的用例随插件走：<c>fmod/Tests/AudioFmodPitchTests.cs</c> 与 <c>wwise/Tests/AudioWwisePitchTests.cs</c>。 <br />
    /// 不依赖 <c>FMOD_INSTALLED</c> / <c>WWISE_INSTALLED</c>，CI 无插件也能跑。
    /// </remarks>
    [TestFixture]
    public sealed class AudioMiddlewarePitchTests
    {
        #region 本地夹具 [LOCAL FIXTURES]

        /// <summary>
        /// 记账用的本地桥——起播合成音高与实时推播都落同一张 <see cref="Pitches"/> 表，供契约用例观测。
        /// </summary>
        private sealed class RecordingPitchBridge : IAudioMiddlewareBridge
        {
            private ulong _nextId = 1UL;

            /// <summary>实例收到的音高（键为桥侧 instanceId）：PlayEvent 记起播值，SetInstancePitch 覆写。</summary>
            public readonly System.Collections.Generic.Dictionary<ulong, float> Pitches =
                new System.Collections.Generic.Dictionary<ulong, float>();

            /// <summary>实时推播触达次数（不含 PlayEvent 记的起播值）。</summary>
            public int LivePitchCount { get; private set; }

            public bool Initialize(UnityEngine.Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }

            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, UnityEngine.Vector3? position3D)
            {
                if (string.IsNullOrEmpty(eventPath)) return 0UL;
                Pitches[_nextId] = pitch;
                return _nextId++;
            }

            public void StopInstance(ulong instanceId) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }

            public void SetInstancePitch(ulong instanceId, float pitch)
            {
                Pitches[instanceId] = pitch;
                LivePitchCount++;
            }

            public void SetBusVolume(string busPath, float volume) { }
            public float GetBusVolume(string busPath) => 0f;
            public bool IsPlaying(ulong instanceId) => true;
            public string GetEventPathFromClip(UnityEngine.AudioClip clip) => null;
        }

        /// <summary>中间件后端的本地替身：音高的存档与推播语义全在框架的 <see cref="MiddlewareAudioHandler"/> 里。</summary>
        private sealed class ContractHandler : MiddlewareAudioHandler
        {
            protected override IAudioMiddlewareBridge CreateDefaultBridge() => new RecordingPitchBridge();
        }

        /// <summary>起播一条 Sfx 声部（音量 1、基础音高 <paramref name="pitch"/>），返回句柄。</summary>
        private static ulong PlaySfx(MiddlewareAudioHandler handler, float pitch)
        {
            var request = new AudioPlayRequest(7, 1f, pitch, EAudioTrack.Sfx, 128, EAudioPlayFlags.None);
            return handler.Play("event:/Sfx/Hit", request, null);
        }

        #endregion 本地夹具 [LOCAL FIXTURES]

        #region 音轨倍率 [TRACK MULTIPLIER]

        [Test]
        public void GetTrackPitch_DefaultsToOne_And_MultipliesIntoNewPlays()
        {
            var bridge = new RecordingPitchBridge();
            var handler = new ContractHandler();
            handler.SetBridge(bridge);

            Assert.AreEqual(AudioGroupConfig.DEFAULT_PITCH, handler.GetTrackPitch(EAudioTrack.Sfx), 1e-5f);

            handler.SetTrackPitch(EAudioTrack.Sfx, 2f);
            Assert.AreNotEqual(0UL, PlaySfx(handler, 0.5f));
            Assert.AreEqual(1f, bridge.Pitches[1], 1e-5f,
                "起播合成音高必须是 基础音高 × 音轨倍率（0.5 × 2）");
        }

        [Test]
        public void SetTrackPitch_ClampsToContractRange()
        {
            var handler = new ContractHandler();
            handler.SetBridge(new RecordingPitchBridge());

            handler.SetTrackPitch(EAudioTrack.Sfx, 100f);
            Assert.AreEqual(AudioGroupConfig.MAXIMAL_PITCH, handler.GetTrackPitch(EAudioTrack.Sfx), 1e-5f);

            handler.SetTrackPitch(EAudioTrack.Sfx, 0f);
            Assert.AreEqual(AudioGroupConfig.MINIMAL_PITCH, handler.GetTrackPitch(EAudioTrack.Sfx), 1e-5f);
        }

        #endregion 音轨倍率 [TRACK MULTIPLIER]

        #region 实时推播 [LIVE PROPAGATION]

        [Test]
        public void SetTrackPitch_PushesToPlayingVoices_OnThatTrackOnly()
        {
            var bridge = new RecordingPitchBridge();
            var handler = new ContractHandler();
            handler.SetBridge(bridge);

            PlaySfx(handler, 1f);
            var musicRequest = new AudioPlayRequest(7, 1f, 1f, EAudioTrack.Music, 128, EAudioPlayFlags.None);
            Assert.AreNotEqual(0UL, handler.Play("event:/Music/Theme", musicRequest, null));

            handler.SetTrackPitch(EAudioTrack.Sfx, 1.5f);
            Assert.AreEqual(1.5f, bridge.Pitches[1], 1e-5f, "Sfx 在播声部必须按新倍率重算");
            Assert.AreEqual(1f, bridge.Pitches[2], 1e-5f, "Music 声部不得被别的音轨的倍率波及");
            Assert.AreEqual(1, bridge.LivePitchCount);

            handler.SetTrackPitch(EAudioTrack.Sfx, 3f);
            Assert.AreEqual(3f, bridge.Pitches[1], 1e-5f);
            Assert.AreEqual(2, bridge.LivePitchCount);
        }

        [Test]
        public void SetVoicePitch_AppliesBaseTimesTrackMultiplier_And_IgnoresUnknownHandle()
        {
            var bridge = new RecordingPitchBridge();
            var handler = new ContractHandler();
            handler.SetBridge(bridge);

            handler.SetTrackPitch(EAudioTrack.Sfx, 2f);
            ulong handle = PlaySfx(handler, 1f);
            Assert.AreEqual(2f, bridge.Pitches[1], 1e-5f);

            handler.SetVoicePitch(handle, 0.5f);
            Assert.AreEqual(1f, bridge.Pitches[1], 1e-5f, "单声部基础音高改写后必须按当前音轨倍率重算");
            Assert.AreEqual(1, bridge.LivePitchCount);

            handler.SetVoicePitch(999UL, 0.5f);
            Assert.AreEqual(1, bridge.LivePitchCount, "未知句柄不得触达桥");
        }

        [Test]
        public void SetVoicePitch_ReleasedHandle_DoesNotTouchBridge()
        {
            var bridge = new RecordingPitchBridge();
            var handler = new ContractHandler();
            handler.SetBridge(bridge);

            ulong handle = PlaySfx(handler, 1f);
            handler.Stop(handle, 0f);

            handler.SetVoicePitch(handle, 0.5f);
            Assert.AreEqual(0, bridge.LivePitchCount, "已释放句柄不得触达桥");
        }

        [Test]
        public void AudioService_SetPitch_DispatchesToHandler()
        {
            // 走生成器发的内部接缝做无副作用换入换出：getter 会懒加载、setter 会 Internal_Init 且拒收 null
            AudioServiceHandler previous = AudioService.Internal_PeekHandler();
            var bridge = new RecordingPitchBridge();
            var handler = new ContractHandler();
            handler.SetBridge(bridge);
            AudioService.Internal_UseHandler(handler);
            try
            {
                ulong handle = PlaySfx(handler, 1f);

                AudioService.SetPitch(handle, 0.5f);
                Assert.AreEqual(0.5f, bridge.Pitches[1], 1e-5f, "音轨倍率默认 1，实际音高等于基础音高");
            }
            finally
            {
                AudioService.Internal_UseHandler(previous);
            }
        }

        #endregion 实时推播 [LIVE PROPAGATION]
    }
}
