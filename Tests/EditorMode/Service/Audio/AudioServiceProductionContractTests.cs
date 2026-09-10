using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// Audio 服务生产级契约测试：句柄语义、Voice Stealing 约定、通道硬上限、播放选项工厂。
    /// </summary>
    public sealed class AudioServiceProductionContractTests
    {
        #region 通道硬上限 [CHANNEL CAP]

        [Test]
        public void AudioCategory_HardChannelCap_IsBounded()
        {
            Assert.GreaterOrEqual(AudioCategory.HARD_CHANNEL_CAP, 8);
            Assert.LessOrEqual(AudioCategory.HARD_CHANNEL_CAP, 128);
        }

        #endregion 通道硬上限 [CHANNEL CAP]

        #region 播放选项热路径约定 [PLAY OPTIONS CONTRACT]

        [Test]
        public void AudioPlayRequest_IsExactly16Bytes()
        {
            Assert.AreEqual(16, System.Runtime.InteropServices.Marshal.SizeOf<AudioPlayRequest>(),
                "热路径请求必须 16 字节");
        }

        [Test]
        public void AudioPlayRequest_PackUnpack_RoundTrip()
        {
            var request = new AudioPlayRequest(42, 0.75f, 1.25f, EAudioTrack.Music, 200,
                AudioPlayFlags.Loop | AudioPlayFlags.Persistent | AudioPlayFlags.FadeInOnPlay);

            Assert.AreEqual(42, request.Id);
            Assert.AreEqual(0.75f, request.Volume, 1e-5f);
            Assert.AreEqual(1.25f, request.Pitch, 1e-5f);
            Assert.AreEqual(EAudioTrack.Music, request.Track);
            Assert.AreEqual(200, request.Priority);
            Assert.IsTrue(request.Loop);
            Assert.IsTrue(request.Persistent);
            Assert.IsTrue(request.FadeInOnPlay);
            Assert.IsFalse(request.SoloAllTracks);
        }

        [Test]
        public void AudioPlayOptions_ToRequest_MapsFlags()
        {
            var options = AudioPlayOptions.CreateLooping(EAudioTrack.Music);
            var request = options.ToRequest();

            Assert.AreEqual(EAudioTrack.Music, request.Track);
            Assert.IsTrue(request.Loop);
            Assert.IsTrue(request.Persistent);
            Assert.IsTrue(request.DoNotAutoRecycleIfNotDonePlaying);
        }

        [Test]
        public void AudioPlayOptions_Create_PopulatesHotFields()
        {
            var options = AudioPlayOptions.Create(EAudioTrack.Sfx);

            Assert.AreEqual(EAudioTrack.Sfx, options.AudioTrack);
            Assert.AreEqual(1f, options.Volume, 1e-5f);
            Assert.AreEqual(1f, options.Pitch, 1e-5f);
            Assert.IsTrue(options.DoNotAutoRecycleIfNotDonePlaying);
            Assert.IsFalse(options.Loop);
            Assert.IsFalse(options.Persistent);
        }

        [Test]
        public void AudioPlayOptions_CreateLooping_MarksPersistentAndLoop()
        {
            var options = AudioPlayOptions.CreateLooping(EAudioTrack.Music);

            Assert.IsTrue(options.Loop);
            Assert.IsTrue(options.Persistent);
            Assert.AreEqual(EAudioTrack.Music, options.AudioTrack);
        }

        [Test]
        public void AudioPlayOptions_CreateWithFade_ConfiguresFadeIn()
        {
            var options = AudioPlayOptions.CreateWithFade(EAudioTrack.Music, 0.5f, 0f, 1f);

            Assert.IsTrue(options.FadeInOnPlay);
            Assert.AreEqual(0.5f, options.FadeInDuration, 1e-5f);
            Assert.AreEqual(0f, options.FadeInInitialVolume, 1e-5f);
            Assert.AreEqual(1f, options.Volume, 1e-5f);
        }

        #endregion 播放选项热路径约定 [PLAY OPTIONS CONTRACT]

        #region 句柄语义 [HANDLE SEMANTICS]

        [Test]
        public void InvalidHandle_IsTreatedAsStopped()
        {
            Assert.IsFalse(AudioService.IsPlaying(0));
            Assert.IsTrue(AudioService.IsStopped(0));
        }

        [Test]
        public void Play_NullClip_ReturnsZeroHandle()
        {
            Assert.AreEqual(0UL, AudioService.Play((AudioClip)null, AudioPlayOptions.Default));
        }

        [Test]
        public void ReleaseHandle_Zero_IsNoOp()
        {
            Assert.DoesNotThrow(() => AudioService.ReleaseHandle(0));
        }

        #endregion 句柄语义 [HANDLE SEMANTICS]

        #region Fade 委托 [FADE DELEGATE CONTRACT]

        [Test]
        public void PlayFade_StopFade_OnUninitializedHandler_DoNotThrow()
        {
            Assert.DoesNotThrow(() => AudioService.PlayFade(1, 0.1f, 0f));
            Assert.DoesNotThrow(() => AudioService.StopFade(1));
        }

        #endregion Fade 委托 [FADE DELEGATE CONTRACT]
    }
}
