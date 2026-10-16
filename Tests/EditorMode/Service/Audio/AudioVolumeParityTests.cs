using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// 跨后端音量语义对拍：同一份输入下两个后端给出同一个契约值，中间件落到总线的数等于 getter 报回的数。
    /// </summary>
    /// <remarks>音轨音量值域统一为 0..10，任一后端写入总线的值都不得超过该值域。</remarks>
    /// <remarks>
    /// 中间件一侧用本文件的本地夹具（<see cref="ParityHandler"/> / <see cref="ParityBridge"/>）：FMOD / Wwise 后端已外置到插件装配， <br />
    /// 本用例锁的是框架契约基类的音量语义，断言与期望值一字未动。
    /// </remarks>
    [TestFixture]
    public sealed class AudioVolumeParityTests
    {
        /// <summary>记录总线音量的本地桥——写入夹取一次、未写过读作 0，与被外置的 Stub 同一口径。</summary>
        private sealed class ParityBridge : IAudioMiddlewareBridge
        {
            private readonly System.Collections.Generic.Dictionary<string, float> _busVolumes =
                new System.Collections.Generic.Dictionary<string, float>(8);
            private ulong _nextId = 1UL;

            public float GetBusVolume(string busPath)
                => _busVolumes.TryGetValue(busPath, out float v) ? v : 0f;

            public void SetBusVolume(string busPath, float volume)
            {
                if (!string.IsNullOrEmpty(busPath)) _busVolumes[busPath] = Mathf.Clamp01(volume);
            }

            public bool Initialize(UnityEngine.Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }

            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, UnityEngine.Vector3? position3D)
                => string.IsNullOrEmpty(eventPath) ? 0UL : _nextId++;

            public void StopInstance(ulong instanceId) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }
            public bool IsPlaying(ulong instanceId) => true;
            public string GetEventPathFromClip(AudioClip clip)
                => clip == null || string.IsNullOrEmpty(clip.name) ? null : "event:/" + clip.name;
        }

        /// <summary>中间件后端的本地替身：只提供抽象的默认桥工厂，音量语义全在框架契约基类里。</summary>
        private sealed class ParityHandler : MiddlewareAudioHandler
        {
            protected override IAudioMiddlewareBridge CreateDefaultBridge() => new ParityBridge();
        }

        private static readonly float[] Inputs = { 1f, 0f, 0.5f, 0.25f, 2.5f, 10f, -3f };

        private static float Expected(float input) => UnityEngine.Mathf.Clamp01(input);

        [Test]
        public void TrackVolume_SameRangeAndSameRoundTrip_OnBothBackends()
        {
            for (int i = 0; i < Inputs.Length; i++)
            {
                float input = Inputs[i];
                float want = Expected(input);

                var config = new AudioGroupConfig { Volume = input };
                Assert.AreEqual(want, config.Volume, 1e-4f,
                    $"Unity 侧 AudioGroupConfig 存了契约外的值（输入 {input}）");

                var stub = new ParityBridge();
                var handler = new ParityHandler();
                handler.SetBridge(stub);
                handler.SetTrackVolume(EAudioTrack.Music, input);

                Assert.AreEqual(want, handler.GetTrackVolume(EAudioTrack.Music), 1e-4f,
                    $"中间件侧 getter 与 Unity 侧不同值（输入 {input}）");
                Assert.AreEqual(want, stub.GetBusVolume("bus:/Music"), 1e-4f,
                    "落到总线上的数必须就是 getter 报回的那个数——第二次夹取就是分歧藏身处");
            }
        }

        [Test]
        public void MasterVolume_SameRangeAndSameRoundTrip_OnBothBackends()
        {
            for (int i = 0; i < Inputs.Length; i++)
            {
                float input = Inputs[i];
                float want = Expected(input);

                var unity = new UnityAudioHandler();
                unity.MasterVolume = input;
                Assert.AreEqual(want, unity.MasterVolume, 1e-4f,
                    $"Unity 侧 MasterVolume 没落在契约值域（输入 {input}）");

                var stub = new ParityBridge();
                var middleware = new ParityHandler();
                middleware.SetBridge(stub);
                middleware.MasterVolume = input;

                Assert.AreEqual(want, middleware.MasterVolume, 1e-4f,
                    $"中间件侧 MasterVolume 与 Unity 侧不同值（输入 {input}）");
                Assert.AreEqual(want, stub.GetBusVolume("bus:/Master"), 1e-4f);
            }
        }

        /// <summary>
        /// 「还没初始化」不等于「后端 inert」：桥接尚未建立的启动窗口里，音量面必须照实报设置值。
        /// </summary>
        /// <remarks>inert 只应在初始化明确失败时成立（见 <see cref="AudioServiceHandler.IsBackendInert"/>），否则启动窗口内读到的 0 会被用户操作写回并持久化。</remarks>
        [Test]
        public void NotYetInitializedBackend_StillReportsItsSettings()
        {
            var noBridge = new ParityHandler();

            noBridge.MasterVolume = 0.8f;
            noBridge.SetTrackVolume(EAudioTrack.Music, 0.6f);

            Assert.AreEqual(0.8f, noBridge.MasterVolume, 1e-4f,
                "桥接未建立就被当成 inert，会把启动期的设置面板全读成 0");
            Assert.AreEqual(0.6f, noBridge.GetTrackVolume(EAudioTrack.Music), 1e-4f);
        }

        [Test]
        public void Mute_KeepsTheSetterValue_OnBothBackends()
        {
            var config = new AudioGroupConfig { Volume = 0.6f };
            config.Mute = true;
            Assert.AreEqual(0.6f, config.Volume, 1e-4f, "静音不得改 getter 报回的设置值（Unity 侧）");

            var stub = new ParityBridge();
            var handler = new ParityHandler();
            handler.SetBridge(stub);
            handler.SetTrackVolume(EAudioTrack.Voice, 0.6f);
            handler.SetTrackMute(EAudioTrack.Voice, true);

            Assert.AreEqual(0.6f, handler.GetTrackVolume(EAudioTrack.Voice), 1e-4f,
                "静音不得改 getter 报回的设置值（中间件侧）");
            Assert.AreEqual(0f, stub.GetBusVolume("bus:/Voice"), 1e-4f, "静音要落到总线上才有意义");

            handler.SetTrackMute(EAudioTrack.Voice, false);
            Assert.AreEqual(0.6f, stub.GetBusVolume("bus:/Voice"), 1e-4f, "取消静音要回到原值而不是满刻度");
        }
    }
}
