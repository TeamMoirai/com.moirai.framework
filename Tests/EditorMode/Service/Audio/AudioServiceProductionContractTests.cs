using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Service.Audio
{
    /// <summary>
    /// Audio 服务生产级契约测试：句柄语义、Voice Stealing 约定、通道硬上限、播放选项工厂。
    /// </summary>
    public sealed class AudioServiceProductionContractTests
    {
        #region 通道硬上限 [CHANNEL CAP]

        [Test]
        public void AudioGroupConfig_ChannelCeiling_IsBoundedAndPerTrack()
        {
            // 上限写死时主机/移动无法各取所需，故必须按音轨配置且有界
            var config = new AudioGroupConfig();
            Assert.AreEqual(AudioGroupConfig.HARD_CHANNEL_CEILING_DEFAULT, config.MaxChannelCeiling,
                "未配置（含改前老资产缺字段）必须落到历史值 32");

            SetCeiling(config, 64);
            Assert.AreEqual(64, config.MaxChannelCeiling);

            SetCeiling(config, 0);
            Assert.AreEqual(AudioGroupConfig.HARD_CHANNEL_CEILING_DEFAULT, config.MaxChannelCeiling,
                "写坏成 0 不得把扩展直接锁死");

            SetCeiling(config, -1);
            Assert.AreEqual(AudioGroupConfig.HARD_CHANNEL_CEILING_DEFAULT, config.MaxChannelCeiling);

            SetCeiling(config, 4096);
            Assert.AreEqual(AudioGroupConfig.HARD_CHANNEL_CEILING_MAX, config.MaxChannelCeiling,
                "越界一律削到天花板，不做静默放大");
        }

        private static void SetCeiling(AudioGroupConfig config, int value) => config.m_MaxChannelCeiling = value;

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
                EAudioPlayFlags.Loop | EAudioPlayFlags.Persistent | EAudioPlayFlags.FadeInOnPlay);

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

        #region 场景停音策略 [SCENE LOAD POLICY]

        [Test]
        public void SceneLoad_Single_AlwaysStopsNonPersistent()
        {
            Assert.IsTrue(AudioServiceHandler.ShouldStopNonPersistentOnSceneLoad(LoadSceneMode.Single),
                "Single 整景切换必须停非持久音");
        }

        [Test]
        public void SceneLoad_Additive_NeverStopsNonPersistent()
        {
            // Additive（叠加/流式分区）加载没有"停掉全部非持久音"的合理用例，永不自动停；
            // 需要收口的游戏流程应在自己的切换点显式 StopAllButPersistent
            Assert.IsFalse(AudioServiceHandler.ShouldStopNonPersistentOnSceneLoad(LoadSceneMode.Additive),
                "Additive 加载永不自动停非持久音");
        }

        #endregion 场景停音策略 [SCENE LOAD POLICY]
    }
}
