using System;
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

        #region 路径播放契约 [PATH PLAY CONTRACT]

        [Test]
        public void Play_PathOverloads_DefaultToAsyncLoad()
        {
            // in 参数在反射中呈 ByRef 类型
            var byRefOptions = typeof(AudioPlayOptions).MakeByRefType();
            var signature = new[] { typeof(string), byRefOptions, typeof(bool), typeof(bool) };

            var facade = typeof(AudioService).GetMethod("Play", signature);
            Assert.IsNotNull(facade, "AudioService.Play(string, in AudioPlayOptions, bool, bool) 应存在");
            Assert.AreEqual(true, facade.GetParameters()[2].DefaultValue,
                "路径播放必须默认异步加载（同步 IO 阻塞主线程，仅限启动期显式传入）");

            var contract = typeof(AudioServiceHandler).GetMethod("Play", signature);
            Assert.IsNotNull(contract, "AudioServiceHandler.Play(string, in AudioPlayOptions, bool, bool) 应存在");
            Assert.AreEqual(true, contract.GetParameters()[2].DefaultValue,
                "抽象契约默认异步加载，外观与后端默认值必须一致");
        }

        #endregion 路径播放契约 [PATH PLAY CONTRACT]

        #region 巨型签名弃用契约 [GIANT PLAY OBSOLETE CONTRACT]

        private const string ObsoleteMessage =
            "使用 Play(AudioClip, in AudioPlayOptions) 或 Play(AudioClip, in AudioPlayRequest, AudioPlayColdParams)";

        private static bool IsGiantPlay(System.Reflection.MethodInfo method)
        {
            if (method.Name != "Play" || method.IsAbstract) return false;
            var parameters = method.GetParameters();
            if (parameters.Length < 3) return false;
            // 巨型签名：首参与次参为 clip/path + EAudioTrack
            bool clipOrPath = parameters[0].ParameterType == typeof(AudioClip)
                              || parameters[0].ParameterType == typeof(string);
            return clipOrPath && parameters[1].ParameterType == typeof(EAudioTrack);
        }

        private static bool IsRecommendedPlay(System.Reflection.MethodInfo method)
        {
            if (method.Name != "Play") return false;
            var parameters = method.GetParameters();
            if (parameters.Length < 2) return false;

            var second = parameters[1].ParameterType;
            if (second.IsByRef) second = second.GetElementType();
            return second == typeof(AudioPlayOptions) || second == typeof(AudioPlayRequest);
        }

        [Test]
        public void Play_GiantOverloads_AreObsoleteWarningOnly()
        {
            foreach (var type in new[] { typeof(AudioService), typeof(AudioServiceHandler) })
            {
                int giantCount = 0;
                foreach (var method in type.GetMethods(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                             | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (!IsGiantPlay(method)) continue;
                    giantCount++;

                    var obsolete = method.GetCustomAttributes(typeof(ObsoleteAttribute), false);
                    Assert.IsNotEmpty(obsolete, $"{type.Name}.{method.Name} 巨型签名必须标记 [Obsolete]");
                    var attr = (ObsoleteAttribute)obsolete[0];
                    Assert.IsFalse(attr.IsError, "仅告警，不得 error:true 阻断编译");
                    Assert.AreEqual(ObsoleteMessage, attr.Message);
                }

                Assert.GreaterOrEqual(giantCount, 2, $"{type.Name} 应至少含 clip/path 两个巨型 Play 重载");
            }
        }

        [Test]
        public void Play_RecommendedOverloads_AreNotObsolete()
        {
            foreach (var type in new[] { typeof(AudioService), typeof(AudioServiceHandler) })
            {
                int recommendedCount = 0;
                foreach (var method in type.GetMethods(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                             | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (!IsRecommendedPlay(method)) continue;
                    recommendedCount++;

                    Assert.IsEmpty(method.GetCustomAttributes(typeof(ObsoleteAttribute), false),
                        $"{type.Name}.Play(Options/Request) 推荐 API 不得标记 [Obsolete]");
                }

                Assert.GreaterOrEqual(recommendedCount, 3, $"{type.Name} 应含 Options/Request 推荐重载");
            }
        }

        #endregion 巨型签名弃用契约 [GIANT PLAY OBSOLETE CONTRACT]
    }
}
