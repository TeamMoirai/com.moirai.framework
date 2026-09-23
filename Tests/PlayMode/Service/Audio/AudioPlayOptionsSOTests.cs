using System.Collections;
using System.Reflection;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// AudioPlayOptionsSO 回归：时间参数（PlaybackTime/PlaybackDuration）必须真的进入播放请求；
    /// 随机曲集下的并发/重播检查必须作用于「本次候选 clip」而非上一曲。
    /// </summary>
    [TestFixture]
    public sealed class AudioPlayOptionsSOTests
    {
        private GameObject _root;
        private AudioServiceTestHost _host;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioPlayOptionsSOTest]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            _host = new AudioServiceTestHost(EAudioTrack.Sfx);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }

        [UnityTest]
        public IEnumerator Play_ConfiguredPlaybackTime_StartsAtConfiguredOffset()
        {
            AudioClip clip = CreateClip("so_time", 3f);
            AudioPlayOptionsSO so = CreateOptions(clip);
            SetField(so, "m_PlaybackTime", new Vector2(0.5f, 0.5f));

            so.Play(Vector3.zero);
            yield return null;

            ulong handle = LastPlayHandle(so);
            Assert.AreNotEqual(0UL, handle, "Play 应产出有效句柄");

            AudioAgent agent = AudioService.GetAgentByHandle(handle);
            Assert.IsNotNull(agent, "句柄应能取回 Agent");
            Assert.IsNotNull(agent.AudioResource, "Agent 应已绑定 AudioSource");
            Assert.That(agent.AudioResource.time, Is.InRange(0.4f, 0.9f),
                "PlaybackTime=0.5 应让起播位置落在 0.5 附近（修复前恒为 0）");

            AudioService.Stop(handle, 0f);
            Object.Destroy(clip);
            Object.Destroy(so);
        }

        [UnityTest]
        public IEnumerator Play_ConfiguredPlaybackDuration_StopsEarly()
        {
            AudioClip clip = CreateClip("so_duration", 3f);
            AudioPlayOptionsSO so = CreateOptions(clip);
            SetField(so, "m_PlaybackDuration", new Vector2(0.2f, 0.2f));

            so.Play(Vector3.zero);
            yield return null;

            ulong handle = LastPlayHandle(so);
            Assert.AreNotEqual(0UL, handle, "Play 应产出有效句柄");
            Assert.IsTrue(AudioService.IsPlaying(handle), "起播后应立即在播");

            // 自定义时长 0.2s + 自然结束淡出 << 1s；3s 整段 clip 若参数丢失此处必然仍在播
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsFalse(AudioService.IsPlaying(handle), "PlaybackDuration=0.2 应让播放提前结束（修复前播完整段）");

            Object.Destroy(clip);
            Object.Destroy(so);
        }

        [UnityTest]
        public IEnumerator Play_MaxConcurrentOnRandomSet_ChecksCandidateNotPrevious()
        {
            AudioClip clipA = CreateClip("so_seq_a", 2f);
            AudioClip clipB = CreateClip("so_seq_b", 2f);
            AudioPlayOptionsSO so = CreateOptions(null);
            SetField(so, "m_RandomAudio", new[] { clipA, clipB });
            SetField(so, "m_SequentialOrder", true);
            SetField(so, "m_MaximumConcurrentInstances", 1);
            SetField(so, "m_Loop", true);

            so.Play(Vector3.zero);
            yield return null;
            ulong first = LastPlayHandle(so);
            Assert.AreNotEqual(0UL, first, "第一次 Play 应产出有效句柄");

            // 顺序模式第二曲为 clipB；并发上限 1 只应统计候选 clipB（在播 0），不得被上一曲 clipA 拦截
            so.Play(Vector3.zero);
            yield return null;
            ulong second = LastPlayHandle(so);

            Assert.AreNotEqual(0UL, second, "第二曲不应被并发上限拦截（修复前检查作用于上一曲 clipA 而误拦）");
            Assert.AreNotEqual(first, second, "两次 Play 应产生不同句柄");
            Assert.AreEqual(1, AudioService.CurrentlyPlayingCount(clipA), "clipA 应仍在播");
            Assert.AreEqual(1, AudioService.CurrentlyPlayingCount(clipB), "clipB 应已成功起播");

            AudioService.StopAll(0f);
            Object.Destroy(clipA);
            Object.Destroy(clipB);
            Object.Destroy(so);
        }

        private static AudioClip CreateClip(string name, float seconds)
        {
            int samples = Mathf.CeilToInt(44100 * seconds);
            var clip = AudioClip.Create(name, samples, 1, 44100, false);
            clip.SetData(new float[samples], 0);
            return clip;
        }

        private static AudioPlayOptionsSO CreateOptions(AudioClip clip)
        {
            var so = ScriptableObject.CreateInstance<AudioPlayOptionsSO>();
            SetField(so, "m_Audio", clip);
            SetField(so, "m_AudioTrack", EAudioTrack.Sfx);
            SetField(so, "m_MaximumConcurrentInstances", -1);
            return so;
        }

        private static ulong LastPlayHandle(AudioPlayOptionsSO so)
        {
            FieldInfo field = typeof(AudioPlayOptionsSO).GetField("_lastPlayHandle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "AudioPlayOptionsSO._lastPlayHandle 字段应存在");
            return (ulong)field.GetValue(so);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{name} 字段应存在");
            field.SetValue(target, value);
        }
    }
}
