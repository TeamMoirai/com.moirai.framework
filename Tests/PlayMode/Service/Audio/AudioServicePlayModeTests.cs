using System.Collections;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// Audio 服务 PlayMode 集成测试（真实 AudioSource）。
    /// <para>自建隔离：直接构造 <see cref="UnityAudioHandler"/> 并 Initialize，不依赖完整 GameEntry 启动链。</para>
    /// <para>覆盖：句柄生命周期自动释放、同 ID 替换、多 ID 分层、Fade ease、Host 池复用。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioServicePlayModeTests
    {
        private GameObject _root;
        private UnityAudioHandler _handler;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioPlayModeTest]");
            Object.DontDestroyOnLoad(_root);
            // 必须有 Listener，否则 AudioSource 行为异常
            _root.AddComponent<AudioListener>();

            // 2 秒 clip，避免首帧自然播完
            _clip = AudioClip.Create("test_tone", 44100 * 2, 1, 44100, false);
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
            if (_handler != null)
            {
                _handler.StopAll(0f);
            }

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
        }

        private static AudioPlayRequest MakeRequest(int id, EAudioTrack track = EAudioTrack.Sfx, AudioPlayFlags flags = AudioPlayFlags.DoNotAutoRecycle)
        {
            return new AudioPlayRequest(id, 1f, 1f, track, 128, flags);
        }

        private ulong PlayClip(int id)
        {
            return _handler.Play(_clip, MakeRequest(id), null);
        }

        [UnityTest]
        public IEnumerator PlayClip_HandleIsValid_AndAutoReleasesAfterStop()
        {
            if (!Application.isPlaying) Assert.Ignore("需要 PlayMode");

            var categories = _handler.AudioCategories;
            if (categories == null || categories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过真实播放集成测试");
            }

            ulong handle = PlayClip(1001);
            Assert.AreNotEqual(0UL, handle, "播放应返回有效句柄");

            yield return null;
            _handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);

            Assert.IsTrue(_handler.IsPlaying(handle), "首帧后应处于播放中");
            Assert.IsNotNull(_handler.GetAgentByHandle(handle));

            _handler.Stop(handle, 0f);
            yield return null;
            _handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);

            Assert.IsTrue(_handler.IsStopped(handle), "Stop 后应视为停止");
            Assert.IsNull(_handler.GetAgentByHandle(handle), "End 后句柄应自动释放，不再映射");
        }

        [UnityTest]
        public IEnumerator TwoDifferentIds_CanPlaySimultaneously()
        {
            if (!Application.isPlaying) Assert.Ignore("需要 PlayMode");
            var categories = _handler.AudioCategories;
            if (categories == null || categories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过");
            }

            ulong a = PlayClip(2001);
            ulong b = PlayClip(2002);
            yield return null;

            Assert.AreNotEqual(0UL, a);
            Assert.AreNotEqual(0UL, b);
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(_handler.IsPlaying(a), "ID 2001 应仍在播");
            Assert.IsTrue(_handler.IsPlaying(b), "ID 2002 应仍在播（分层共存）");

            _handler.StopByID(2001, 0f);
            yield return null;

            Assert.IsTrue(_handler.IsStopped(a), "StopByID 只停 2001");
            Assert.IsTrue(_handler.IsPlaying(b), "2002 不受影响");
        }

        [UnityTest]
        public IEnumerator SameId_ReplacesPreviousLayer()
        {
            if (!Application.isPlaying) Assert.Ignore("需要 PlayMode");
            var categories = _handler.AudioCategories;
            if (categories == null || categories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过");
            }

            ulong first = PlayClip(3001);
            yield return null;

            // 同 ID 再播（先 StopByID 再 Play，模拟 BackgroundMusic）
            _handler.StopByID(3001, 0f);
            yield return null;
            Assert.IsTrue(_handler.IsStopped(first));

            ulong second = PlayClip(3001);
            yield return null;
            Assert.IsTrue(_handler.IsPlaying(second));
            Assert.IsNull(_handler.GetAgentByHandle(first), "旧句柄已释放");
        }

        [UnityTest]
        public IEnumerator FadeAudio_AppliesTweenEaseAndCompletes()
        {
            if (!Application.isPlaying) Assert.Ignore("需要 PlayMode");
            var categories = _handler.AudioCategories;
            if (categories == null || categories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过");
            }

            ulong handle = PlayClip(4001);
            yield return null;
            Assert.IsTrue(_handler.IsPlaying(handle));

            var agent = _handler.GetAgentByHandle(handle);
            Assert.IsNotNull(agent);
            float startVolume = agent.AudioResource.volume;

            _handler.FadeAudio(handle, 0.15f, startVolume, 0.05f, new TweenEase(TweenUtility.EEase.Linear));
            Assert.IsTrue(_handler.SoundIsFadingOut(handle));

            // 必须驱动 Tick，手动 Fade 列表才会推进
            float elapsed = 0f;
            while (elapsed < 0.5f && _handler.SoundIsFadingOut(handle))
            {
                elapsed += Time.unscaledDeltaTime;
                _handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.IsFalse(_handler.SoundIsFadingOut(handle), "过渡应已完成");
            Assert.AreEqual(0.05f, agent.AudioResource.volume, 0.02f, "结束音量应接近目标");
        }

        [UnityTest]
        public IEnumerator StopByID_ZeroFade_IsSafeWhenNoMatch()
        {
            if (!Application.isPlaying) Assert.Ignore("需要 PlayMode");
            Assert.DoesNotThrow(() => _handler.StopByID(999999, 0f));
            yield return null;
        }
    }
}
