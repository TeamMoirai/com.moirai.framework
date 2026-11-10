using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// FMOD 插件后端的音高契约：Stub 记账语义、音轨倍率乘入起播与实时推播到在播声部。
    /// </summary>
    /// <remarks>
    /// 随 <c>fmod</c> 插件走的本后端专属用例，从框架的 <c>AudioMiddlewarePitchTests</c> 平移；派发语义全在框架的
    /// <see cref="MiddlewareAudioHandler"/> 里，这里用 <see cref="FmodBridgeStub"/> 观测落到桥的合成音高。 <br />
    /// 不依赖 <c>FMOD_INSTALLED</c>：宏关时编的是 <see cref="FmodBridgeStub"/>，Native 桥的实时音高走
    /// <c>EventInstance.setPitch</c>（受宏保护，无法编译核对，见 <c>FmodBridgeNative.SetInstancePitch</c>）。
    /// </remarks>
    [TestFixture]
    public sealed class AudioFmodPitchTests
    {
        #region Stub 契约 [STUB CONTRACT]

        [Test]
        public void FmodStub_PlayEvent_RecordsStartupPitch_WithoutTouchingLiveCounter()
        {
            var stub = new FmodBridgeStub();
            ulong id = stub.PlayEvent("event:/Hit", 1f, 0.5f, false, null);

            Assert.AreNotEqual(0UL, id);
            Assert.AreEqual(0.5f, stub.InstancePitches[id], 1e-5f);
            Assert.AreEqual(0, stub.PitchCount, "起播音高不算实时推播");
        }

        [Test]
        public void FmodStub_SetInstancePitch_OverwritesAndCounts()
        {
            var stub = new FmodBridgeStub();
            ulong id = stub.PlayEvent("event:/Hit", 1f, 1f, false, null);

            stub.SetInstancePitch(id, 1.5f);
            stub.SetInstancePitch(id, 2f);

            Assert.AreEqual(2f, stub.InstancePitches[id], 1e-5f, "同实例的实时改写必须覆盖旧值");
            Assert.AreEqual(2, stub.PitchCount);
        }

        [Test]
        public void FmodStub_Shutdown_ClearsPitchLedger()
        {
            var stub = new FmodBridgeStub();
            stub.PlayEvent("event:/Hit", 1f, 1f, false, null);
            stub.Shutdown();

            Assert.AreEqual(0, stub.InstancePitches.Count);
        }

        #endregion Stub 契约 [STUB CONTRACT]

        #region Handler 派发 [HANDLER FACADE DISPATCH]

        [Test]
        public void MiddlewareHandler_SetTrackPitch_MultipliesIntoNewPlays()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);

            Assert.AreEqual(AudioGroupConfig.DEFAULT_PITCH, handler.GetTrackPitch(EAudioTrack.Sfx), 1e-5f);

            handler.SetTrackPitch(EAudioTrack.Sfx, 2f);
            var request = new AudioPlayRequest(7, 1f, 0.5f, EAudioTrack.Sfx, 128, EAudioPlayFlags.None);
            Assert.AreNotEqual(0UL, handler.Play("event:/Hit", request, null));
            Assert.AreEqual(1f, stub.InstancePitches[1], 1e-5f,
                "起播合成音高必须是 基础音高 × 音轨倍率（0.5 × 2）");
        }

        [Test]
        public void MiddlewareHandler_SetTrackPitch_PushesToPlayingVoices()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);

            var request = new AudioPlayRequest(7, 1f, 1f, EAudioTrack.Sfx, 128, EAudioPlayFlags.Loop);
            Assert.AreNotEqual(0UL, handler.Play("event:/Hit", request, null));

            handler.SetTrackPitch(EAudioTrack.Sfx, 1.5f);
            Assert.AreEqual(1.5f, stub.InstancePitches[1], 1e-5f, "在播实例必须按新倍率实时重算");
            Assert.AreEqual(1, stub.PitchCount);
        }

        [Test]
        public void MiddlewareHandler_SetVoicePitch_AppliesBaseTimesTrack_And_IgnoresUnknownHandle()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);

            handler.SetTrackPitch(EAudioTrack.Sfx, 2f);
            var request = new AudioPlayRequest(7, 1f, 1f, EAudioTrack.Sfx, 128, EAudioPlayFlags.None);
            ulong handle = handler.Play("event:/Hit", request, null);
            Assert.AreEqual(2f, stub.InstancePitches[1], 1e-5f);

            handler.SetVoicePitch(handle, 0.5f);
            Assert.AreEqual(1f, stub.InstancePitches[1], 1e-5f, "单声部基础音高改写后必须按当前音轨倍率重算");

            handler.SetVoicePitch(999UL, 0.5f);
            Assert.AreEqual(1, stub.PitchCount, "未知句柄不得触达桥");
        }

        #endregion Handler 派发 [HANDLER FACADE DISPATCH]
    }
}
