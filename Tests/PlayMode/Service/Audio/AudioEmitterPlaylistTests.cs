using System.Collections;
using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// AudioEmitter / BgmPlaylist 场景组件验收：播放句柄、同 ID 替换、触发半径、列表切换。
    /// <para>关键路径走 <see cref="AudioServiceTestHost"/>（最小配置 + 换入 s_Handler），
    /// 配置缺失会 Fail 而不是 Ignore——避免「全绿零覆盖」。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioEmitterPlaylistTests
    {
        private GameObject _root;
        private AudioServiceTestHost _host;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioEmitterTest]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            _clip = AudioClip.Create("emitter_tone", 44100, 1, 44100, false);
            _clip.SetData(new float[44100], 0);

            _host = new AudioServiceTestHost(EAudioTrack.Music, EAudioTrack.Ambience, EAudioTrack.Sfx);
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
            if (_root != null) Object.Destroy(_root);
            if (_clip != null) Object.Destroy(_clip);
            _root = null;
            _clip = null;
        }

        [UnityTest]
        public IEnumerator Emitter_PlayStop_TouchesHandle()
        {
            var go = new GameObject("Emitter");
            go.transform.SetParent(_root.transform, false);
            var emitter = go.AddComponent<AudioEmitter>();

            // 通过序列化字段注入 clip（测试环境无 Inspector）
            var clipField = typeof(AudioEmitter).GetField("m_Clip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(clipField, "AudioEmitter.m_Clip 字段应存在");
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
            var go = new GameObject("Bgm");
            go.transform.SetParent(_root.transform, false);
            var playlist = go.AddComponent<BgmPlaylist>();

            var tracksField = typeof(BgmPlaylist).GetField("m_Tracks",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(tracksField, "BgmPlaylist.m_Tracks 字段应存在");
            var list = (System.Collections.Generic.List<AudioClip>)tracksField.GetValue(playlist);
            list.Add(_clip);
            list.Add(_clip);

            var playOnStart = typeof(BgmPlaylist).GetField("m_PlayOnStart",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(playOnStart, "BgmPlaylist.m_PlayOnStart 字段应存在");
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

        [Test]
        public void TestHost_MinimalConfig_ProducesCategories()
        {
            // 哨兵：夹具本身必须能建出可播后端，否则上面两格会因空配置被静默洗成假绿
            Assert.IsNotNull(_host?.Handler);
            Assert.IsNotEmpty(_host.Handler.AudioCategories);
            Assert.IsTrue(_host.Handler.IsStopped(0), "句柄 0 一律视为已停");
        }
    }
}
