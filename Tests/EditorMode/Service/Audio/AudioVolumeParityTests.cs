using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 跨后端音量语义对拍：同一份输入，Unity 侧的存储与中间件侧的 Handler 必须给出**同一个契约值**，
    /// 且中间件落到总线上的数就等于 getter 报回的数。
    /// <para>补这一件是因为音轨音量曾长期两边不一致：Unity 允许 0..10（Mixer db 换算），中间件存 0..10
    /// 却在写总线时 <c>Clamp01</c> —— 同一份 <c>AudioServiceSettings</c> 换后端，音轨上限从 10 变成 1。
    /// 这类分歧不会因为"两边各自实现同一份契约成员"而消失，只有对拍用例能把它钉住。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioVolumeParityTests
    {
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

                var stub = new FmodBridgeStub();
                var handler = new FmodAudioHandler();
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

                var stub = new FmodBridgeStub();
                var middleware = new FmodAudioHandler();
                middleware.SetBridge(stub);
                middleware.MasterVolume = input;

                Assert.AreEqual(want, middleware.MasterVolume, 1e-4f,
                    $"中间件侧 MasterVolume 与 Unity 侧不同值（输入 {input}）");
                Assert.AreEqual(want, stub.GetBusVolume("bus:/Master"), 1e-4f);
            }
        }

        /// <summary>
        /// 「还没初始化」不等于「后端 inert」：桥接尚未建立的启动窗口里，音量面必须照实报设置值。
        /// <para>钉这一格是因为最省事的 inert 写法是 <c>_bridge == null</c>，而那会在玩家构建里
        /// 造出一条真实损坏路径：初始化完成前设置面板被打开，滑杆全读成 0，用户一动就把 0 写回并持久化。
        /// 只有"初始化明确失败"才该转 inert（见 <see cref="AudioServiceHandler.IsBackendInert"/>）。</para>
        /// </summary>
        [Test]
        public void NotYetInitializedBackend_StillReportsItsSettings()
        {
            var noBridge = new FmodAudioHandler();

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

            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
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
