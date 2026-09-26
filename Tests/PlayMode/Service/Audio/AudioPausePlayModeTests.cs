using System.Collections;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 暂停与音轨门控的 PlayMode 集成测试（真实 AudioSource + 真实斜坡计时）。
    /// <para>沿用 <see cref="AudioServicePlayModeTests"/> 的自建隔离：直接构造 <see cref="UnityAudioHandler"/>
    /// 并反射调 OnInit，不依赖完整 GameEntry 启动链。</para>
    /// <para>斜坡计时走 <c>GameTime.unscaledTime</c>，因此等待一律用真实秒数。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioPausePlayModeTests
    {
        private GameObject _root;
        private UnityAudioHandler _handler;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioPausePlayModeTest]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            // 2 秒单声道正弦，避免测到一半自然播完
            _clip = AudioClip.Create("pause_test_tone", 44100 * 2, 1, 44100, false);
            var data = new float[44100 * 2];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Mathf.Sin(i * 0.05f) * 0.1f;
            }

            _clip.SetData(data, 0);

            _handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(init, "UnityAudioHandler.OnInit 应存在");
            init.Invoke(_handler, null);
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.StopAll(0f);
            _handler?.UnpauseAll();

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            if (_clip != null)
            {
                Object.Destroy(_clip);
                _clip = null;
            }

            _handler = null;
            AudioListener.volume = 1f;
            AudioListener.pause = false;
        }

        private void RequireTracks()
        {
            var categories = _handler.AudioCategories;
            if (categories == null || categories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过真实播放集成测试");
            }
        }

        private ulong PlayFadeIn(float duration)
        {
            var options = AudioPlayOptions.CreateWithFade(EAudioTrack.Sfx, duration);
            options.ID = 7001;
            return _handler.Play(_clip, options);
        }

        private ulong PlayPlain()
        {
            return _handler.Play(_clip, new AudioPlayRequest(7002, 1f, 1f, EAudioTrack.Sfx, 128,
                EAudioPlayFlags.DoNotAutoRecycle), null);
        }

        /// <summary>推进真实秒数并驱动服务 Tick（淡入淡出按 unscaled 计时）。</summary>
        private IEnumerator Advance(float seconds)
        {
            const int steps = 3;
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForSecondsRealtime(seconds / steps);
                _handler.Tick(seconds / steps, seconds / steps);
            }
        }

        [UnityTest]
        public IEnumerator PauseDuringFadeIn_FreezesRamp_UnpauseResumesIt()
        {
            RequireTracks();

            ulong handle = PlayFadeIn(0.6f);
            Assert.AreNotEqual(0UL, handle, "淡入播放应返回有效句柄");
            yield return Advance(0.18f);

            var agent = _handler.GetAgentByHandle(handle);
            Assert.IsNotNull(agent);
            float ramped = agent.AudioResource.volume;
            Assert.Greater(ramped, 0.001f, "淡入应已抬升音量");
            Assert.Less(ramped, 0.99f, "淡入不应已经走完");

            _handler.Pause(handle);
            Assert.IsTrue(agent.IsPaused, "暂停后应处于 Pausing");
            yield return Advance(0.25f);
            Assert.AreEqual(ramped, agent.AudioResource.volume, 0.001f, "暂停期间音量斜坡必须冻结");

            _handler.Unpause(handle);
            Assert.IsFalse(agent.IsPaused);
            yield return Advance(0.45f);

            // 旧实现把 Pausing 一律还原成 Playing，斜坡被丢弃、音量永远停在暂停处
            Assert.Greater(agent.AudioResource.volume, ramped + 0.2f,
                "恢复后淡入必须继续爬升而不是停在暂停时的音量");
            _handler.Stop(handle, 0f);
        }

        [UnityTest]
        public IEnumerator PauseDuringFadeOut_ResumesFadeInsteadOfResurrecting()
        {
            RequireTracks();

            ulong handle = PlayPlain();
            Assert.AreNotEqual(0UL, handle);
            yield return Advance(0.05f);

            _handler.Stop(handle, 0.5f);
            yield return Advance(0.12f);

            var agent = _handler.GetAgentByHandle(handle);
            Assert.IsNotNull(agent, "淡出中的声部仍被句柄绑定");
            Assert.IsTrue(agent.AudioResource.volume < 0.99f, "淡出应已压低音量");

            _handler.Pause(handle);
            yield return Advance(0.35f); // 暂停期间斜坡冻结，这段时间不计入淡出进度

            _handler.Unpause(handle);
            yield return Advance(0.5f); // 剩余淡出 0.38s，必须留足余量才能观察到"走完并停播"

            // 旧实现恢复成 Playing：一条正在淡出的音会复活成满音量常播
            Assert.IsFalse(agent.IsPlaying, "淡出恢复后应走完并停播，而不是回到常播");
            Assert.IsTrue(_handler.IsStopped(handle), "句柄应在淡出结束后释放");
        }

        [UnityTest]
        public IEnumerator Pause_HoldsPlayPosition_UnpauseAdvancesIt()
        {
            RequireTracks();

            ulong handle = PlayPlain();
            yield return Advance(0.1f);
            _handler.Pause(handle);

            var agent = _handler.GetAgentByHandle(handle);
            int held = agent.AudioResource.timeSamples;
            yield return Advance(0.2f);
            Assert.AreEqual(held, agent.AudioResource.timeSamples, "暂停期间不应推进播放位置");

            _handler.Unpause(handle);
            yield return Advance(0.2f);
            Assert.Greater(agent.AudioResource.timeSamples, held, "恢复后应继续推进");
            _handler.Stop(handle, 0f);
        }

        [UnityTest]
        public IEnumerator PauseTrack_BlocksNewPlay_UnpauseTrackRestoresIt()
        {
            RequireTracks();

            _handler.PauseTrack(EAudioTrack.Sfx);
            Assert.IsTrue(_handler.IsPaused(EAudioTrack.Sfx));
            Assert.AreEqual(0UL, PlayPlain(), "暂停中的音轨必须拦截新播放");

            _handler.UnpauseTrack(EAudioTrack.Sfx);
            Assert.IsFalse(_handler.IsPaused(EAudioTrack.Sfx));

            ulong handle = PlayPlain();
            Assert.AreNotEqual(0UL, handle, "恢复后应可正常播放");
            yield return Advance(0.05f);
            Assert.IsTrue(_handler.IsPlaying(handle));
            _handler.Stop(handle, 0f);
        }

        [UnityTest]
        public IEnumerator PauseAll_ThenUnpauseAll_LeavesNothingStuckPaused()
        {
            RequireTracks();

            ulong first = PlayPlain();
            Assert.AreNotEqual(0UL, first);
            ulong second = PlayFadeIn(0.6f);
            Assert.AreNotEqual(0UL, second);
            yield return Advance(0.1f);

            _handler.PauseAll();
            yield return Advance(0.1f);
            _handler.UnpauseAll();
            yield return Advance(0.1f);

            Assert.IsTrue(_handler.IsPlaying(first), "PauseAll/UnpauseAll 后首条应回到播放");
            var fadeAgent = _handler.GetAgentByHandle(second);
            Assert.IsNotNull(fadeAgent);
            Assert.IsFalse(fadeAgent.IsPaused, "淡入中的声部不应留在 Pausing");
            Assert.Greater(fadeAgent.AudioResource.volume, 0.001f);

            _handler.StopAll(0f);
        }

        [UnityTest]
        public IEnumerator StopWhilePaused_ReleasesHandleAndFreesAgent()
        {
            RequireTracks();

            ulong handle = PlayPlain();
            yield return Advance(0.05f);
            _handler.Pause(handle);

            _handler.Stop(handle, 0f);
            yield return Advance(0.05f);

            Assert.IsTrue(_handler.IsStopped(handle), "暂停中的句柄也应被 Stop 收口");
            Assert.IsNull(_handler.GetAgentByHandle(handle), "句柄映射应已解除");
        }

        [UnityTest]
        public IEnumerator Shutdown_ExternalListenerPause_StateAlwaysCleared()
        {
            RequireTracks();

            // 模拟非本框架渠道置位（_pausedByFramework 保持 false）——OnShutdown 必须无条件复位，
            // 否则 AudioListener.pause=true 会寄生到下一个场景或编辑器会话。
            // 夹具经反射直调 OnInit（绕过 Internal_Init，_initialized=false），
            // Internal_Shutdown 会因此空转；与 SetUp 同构地直调 OnShutdown 才能测到本契约。
            AudioListener.pause = true;
            var shutdown = typeof(UnityAudioHandler)
                .GetMethod("OnShutdown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(shutdown, "UnityAudioHandler.OnShutdown 应存在");
            shutdown.Invoke(_handler, null);
            Assert.IsFalse(AudioListener.pause, "外部置位的 AudioListener.pause 也必须在 OnShutdown 复位");

            yield break;
        }
    }
}
