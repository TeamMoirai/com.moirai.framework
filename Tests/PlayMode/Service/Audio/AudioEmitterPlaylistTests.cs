using System.Collections;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// AudioEmitter / BgmPlaylist 场景组件验收：播放句柄、同 ID 替换、触发半径、列表切换。
    /// </summary>
    [TestFixture]
    public sealed class AudioEmitterPlaylistTests
    {
        private GameObject _root;
        private UnityAudioHandler _handler;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioEmitterTest]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            _clip = AudioClip.Create("emitter_tone", 44100, 1, 44100, false);
            _clip.SetData(new float[44100], 0);

            _handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(_handler, null);
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.StopAll(0f);
            if (_root != null) Object.Destroy(_root);
            if (_clip != null) Object.Destroy(_clip);
            _handler = null;
            _root = null;
            _clip = null;
        }

        [UnityTest]
        public IEnumerator Emitter_PlayStop_TouchesHandle()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置");
                yield break;
            }

            var go = new GameObject("Emitter");
            go.transform.SetParent(_root.transform, false);
            var emitter = go.AddComponent<AudioEmitter>();

            // 通过序列化字段注入 clip（测试环境无 Inspector）
            var clipField = typeof(AudioEmitter).GetField("m_Clip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            clipField.SetValue(emitter, _clip);

            emitter.Play();
            yield return null;

            Assert.IsTrue(emitter.IsPlaying, "Play 后应在播");
            Assert.AreNotEqual(0UL, emitter.Handle);

            emitter.Stop();
            yield return null;
            Assert.IsFalse(emitter.IsPlaying, "Stop 后应停");
        }

        [UnityTest]
        public IEnumerator Playlist_SameId_ReplacesPreviousLayer()
        {
            if (_handler.AudioCategories == null || _handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置");
                yield break;
            }

            var go = new GameObject("Bgm");
            go.transform.SetParent(_root.transform, false);
            var playlist = go.AddComponent<BgmPlaylist>();

            var tracksField = typeof(BgmPlaylist).GetField("m_Tracks",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var list = (System.Collections.Generic.List<AudioClip>)tracksField.GetValue(playlist);
            list.Add(_clip);
            list.Add(_clip);

            var playOnStart = typeof(BgmPlaylist).GetField("m_PlayOnStart",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            playOnStart.SetValue(playlist, false);

            playlist.PlayIndex(0);
            yield return null;
            ulong first = (ulong)typeof(BgmPlaylist)
                .GetField("_handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(playlist);
            Assert.AreNotEqual(0UL, first);

            playlist.PlayIndex(1);
            yield return null;
            ulong second = (ulong)typeof(BgmPlaylist)
                .GetField("_handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(playlist);
            Assert.AreNotEqual(0UL, second);
            Assert.AreNotEqual(first, second, "切歌应产生新句柄（同 ID 替换）");

            playlist.Stop();
        }

        [Test]
        public void Emitter_ComponentExists_OnMenuPath()
        {
            var menu = typeof(AudioEmitter).GetCustomAttributes(typeof(AddComponentMenu), false);
            Assert.IsNotEmpty(menu);
        }
    }
}
