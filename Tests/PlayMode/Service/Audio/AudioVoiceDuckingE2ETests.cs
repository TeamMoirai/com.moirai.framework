using System.Collections;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 自动 Ducking 端到端：Voice 有声 → Dialogue 快照；播完 → 回落。
    /// <para>用可注入的 Snapshot 配置与 Mix 状态机驱动，不依赖项目 Mixer 资产。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioVoiceDuckingE2ETests
    {
        private GameObject _root;
        private UnityAudioHandler _handler;
        private AudioClip _voiceClip;
        private bool _prevDucking;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioDuckTest]");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            _voiceClip = AudioClip.Create("voice_tone", 44100, 1, 44100, false);
            _voiceClip.SetData(new float[44100], 0);

            _handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(_handler, null);

            // 状态机只在过渡真的能施加时才推进状态：测试环境的项目 Mixer 未必带 Dialogue/Default 快照，
            // 缺快照时 Ducking 的请求会被正当拒绝、Current 永远停在 Default，端到端断言便无从观测。
            // 这里挂上生产就有的中间件过渡接缝（只记账、不碰真实混音），让切/回落语义可验证。
            // 必须在 OnInit 之后：OnInit 会经 AudioMixService.Initialize 重建状态机。
            AudioMixService.SetMiddlewareTransitionHandler((state, blendSeconds) => { });

            // 打开自动 Ducking（Settings 可能在测试环境无资产实例——用反射强写）
            _prevDucking = ReadDuckingEnabled();
            ForceDuckingEnabled(true);
            AudioVoiceDucking.Reset();
            AudioMixService.ResetToDefault(0f);
        }

        [TearDown]
        public void TearDown()
        {
            AudioVoiceDucking.Reset();
            AudioMixService.SetMiddlewareTransitionHandler(null);
            ForceDuckingEnabled(_prevDucking);
            _handler?.StopAll(0f);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_voiceClip != null) UnityEngine.Object.Destroy(_voiceClip);
            _handler = null;
            _root = null;
            _voiceClip = null;
        }

        private static bool ReadDuckingEnabled()
        {
            try
            {
                return AudioServiceSettings.AutoDuckingOnVoice;
            }
            catch
            {
                return false;
            }
        }

        private static void ForceDuckingEnabled(bool enabled)
        {
            AudioServiceSettings instance = FrameworkSettings<AudioServiceSettings>.Instance;
            if (instance == null) return;

            instance.m_AutoDuckingOnVoice = enabled;
        }

        [UnityTest]
        public IEnumerator VoiceStart_SwitchesToDialogue_VoiceEnd_ReturnsToDefault()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过 Ducking 端到端");
                yield break;
            }

            // 注册 Dialogue 快照（无真实 AudioMixerSnapshot 时状态机仍会切状态）
            AudioMixService.Request(EMixSnapshot.Default, 0f, force: true);
            Assert.AreEqual(EMixSnapshot.Default, AudioMixService.Current);

            var options = AudioPlayOptions.Create(EAudioTrack.Voice);
            options.ID = 500;
            options.DoNotAutoRecycleIfNotDonePlaying = true;
            options.Loop = true;

            ulong handle = _handler.Play(_voiceClip, options);
            if (handle == 0UL)
            {
                Assert.Ignore("Voice 轨未配置或无可用声部");
                yield break;
            }

            _handler.Tick(0f, 0.016f);

            if (!AudioServiceSettings.AutoDuckingOnVoice)
            {
                _handler.Stop(handle, 0f);
                Assert.Ignore("AutoDuckingOnVoice 未开启（测试环境 Settings 可能不可写）");
                yield break;
            }

            Assert.AreEqual(EMixSnapshot.Dialogue, AudioMixService.Current,
                "Voice 在播时应切入 Dialogue 快照");

            _handler.Stop(handle, 0f);
            _handler.Tick(0f, 0.016f);

            Assert.AreNotEqual(EMixSnapshot.Dialogue, AudioMixService.Current,
                "Voice 播完后应回落，不再占用 Dialogue");
        }

        [UnityTest]
        public IEnumerator HighPrioritySnapshot_OwnedByElsewhere_DuckDoesNotSteal()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置");
                yield break;
            }

            // 先切到更高优先级的 Cinematic
            bool switched = AudioMixService.Request(EMixSnapshot.Cinematic, 0f, force: true);
            if (!switched)
            {
                Assert.Ignore("Cinematic 快照未注册");
                yield break;
            }

            var options = AudioPlayOptions.Create(EAudioTrack.Voice);
            options.ID = 501;
            options.Loop = true;
            options.DoNotAutoRecycleIfNotDonePlaying = true;

            ulong handle = _handler.Play(_voiceClip, options);
            if (handle == 0UL)
            {
                Assert.Ignore("Voice 轨不可用");
                yield break;
            }

            _handler.Tick(0f, 0.016f);
            Assert.AreEqual(EMixSnapshot.Cinematic, AudioMixService.Current,
                "更高优先级状态不被自动 Ducking 打断");

            _handler.Stop(handle, 0f);
            _handler.Tick(0f, 0.016f);
        }

        [UnityTest]
        public IEnumerator ToggleOff_MidDuck_RestoresMix()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置");
                yield break;
            }

            AudioMixService.Request(EMixSnapshot.Default, 0f, force: true);

            var options = AudioPlayOptions.Create(EAudioTrack.Voice);
            options.ID = 502;
            options.Loop = true;
            options.DoNotAutoRecycleIfNotDonePlaying = true;

            ulong handle = _handler.Play(_voiceClip, options);
            if (handle == 0UL)
            {
                Assert.Ignore("Voice 轨不可用");
                yield break;
            }

            _handler.Tick(0f, 0.016f);

            ForceDuckingEnabled(false);
            _handler.Tick(0f, 0.016f);

            Assert.AreNotEqual(EMixSnapshot.Dialogue, AudioMixService.Current,
                "运行中关闭自动 Ducking 必须回落，不得卡在 Dialogue");

            _handler.Stop(handle, 0f);
        }
    }
}
