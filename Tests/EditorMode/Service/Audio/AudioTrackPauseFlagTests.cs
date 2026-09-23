using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// 音轨暂停标记的契约级用例。标记数组与"取数组 + 判空 + 判界"此前在两后端各存一份，
    /// 而契约第 1 条（暂停的音轨拦截新播放）正是压在这份状态上的——状态有两个所有者，规则就有两份实现。
    /// <para>这里锁三件事：标记按音轨逐个生效不串味、**中间件侧**的"暂停拦播放"（这条此前只有 Unity
    /// 的 <c>AudioPausePlayModeTests</c> 证过，而契约说的是两后端一致）、以及 Unity 侧"数组未分配即整段作废"
    /// 这条被刻意保留的行为（分配点留在后端，见 <see cref="AudioServiceHandler._pausedTracks"/> 的 remarks；
    /// 谁把它顺手改成基类懒分配，本用例当场红）。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioTrackPauseFlagTests
    {
        private AudioClip _clip;
        private FmodAudioHandler _middleware;

        [SetUp]
        public void SetUp()
        {
            _clip = AudioClip.Create("PauseFlagProbe", 1, 1, 44100, false);
            _middleware = new FmodAudioHandler();
            _middleware.SetBridge(new FmodBridgeStub());
        }

        [TearDown]
        public void TearDown()
        {
            if (_clip != null) UnityEngine.Object.DestroyImmediate(_clip);
        }

        private ulong PlayOn(EAudioTrack track) => _middleware.Play(_clip, AudioPlayOptions.Create(track));

        [Test]
        public void PauseTrack_RoundTrips_WithoutBleedingIntoOtherTracks()
        {
            _middleware.PauseTrack(EAudioTrack.Music);

            Assert.IsTrue(_middleware.IsPaused(EAudioTrack.Music), "置位没记上");
            Assert.IsFalse(_middleware.IsPaused(EAudioTrack.Sfx), "暂停一条音轨串到了别条");
            Assert.IsFalse(_middleware.IsPaused(EAudioTrack.Voice), "暂停一条音轨串到了别条");

            _middleware.UnpauseTrack(EAudioTrack.Music);
            Assert.IsFalse(_middleware.IsPaused(EAudioTrack.Music), "复位没生效");
        }

        [Test]
        public void PausedTrack_RefusesNewPlay_OnTheMiddlewareBackendToo()
        {
            Assert.AreNotEqual(0UL, PlayOn(EAudioTrack.Music), "前提：未暂停时应能播放");

            _middleware.PauseTrack(EAudioTrack.Music);
            Assert.AreEqual(0UL, PlayOn(EAudioTrack.Music), "暂停中的音轨必须拦截新播放（契约第 1 条）");

            Assert.AreNotEqual(0UL, PlayOn(EAudioTrack.Sfx), "拦截只该落在被暂停的那条音轨上");

            _middleware.UnpauseTrack(EAudioTrack.Music);
            Assert.AreNotEqual(0UL, PlayOn(EAudioTrack.Music), "恢复后应可正常播放");
        }

        [Test]
        public void TrackOutsideTheFlagTable_IsNotReportedPaused()
        {
            // 越界下标一律"未暂停"。两后端对越界 track 的**播放**处理本就不一致（中间件在入口处直接拒绝，
            // Unity 会放行再到 Category 缓存里落空），这条差异不在重构里顺手抹平，只把标记查询的口径钉住。
            Assert.IsFalse(_middleware.IsPaused((EAudioTrack)200));

            _middleware.PauseTrack((EAudioTrack)200);
            Assert.IsFalse(_middleware.IsPaused((EAudioTrack)200), "越界置位应当被忽略而不是越界写");
            Assert.IsFalse(_middleware.IsPaused(EAudioTrack.Sfx), "越界置位串进了合法音轨");
        }

        [Test]
        public void UnityBackend_DropsPauseRequestsUntilItsTableIsBuilt()
        {
            // Unity 侧的标记数组在 Initialize 里建（且要 Application.isPlaying），编辑器裸建实例时它是 null。
            // "初始化前调 PauseTrack 会被忘掉"是原行为，上移标记所有权时刻意没有改成懒分配。
            var unity = new UnityAudioHandler();

            unity.PauseTrack(EAudioTrack.Music);
            Assert.IsFalse(unity.IsPaused(EAudioTrack.Music),
                "Unity 侧未分配数组时不应凭空记上——分配点仍在后端，改成基类懒分配就是行为变更");
        }
    }
}
