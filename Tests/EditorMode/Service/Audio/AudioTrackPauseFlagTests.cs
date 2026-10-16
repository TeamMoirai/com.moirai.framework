using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// 音轨暂停标记的契约级用例：暂停的音轨拦截新播放，两后端行为一致。
    /// </summary>
    /// <remarks>
    /// 锁三件事：标记按音轨逐个生效不串味、中间件侧的暂停拦播放、以及 Unity 侧「数组未分配即整段作废」这条刻意保留的行为
    /// （分配点留在后端，见 <see cref="AudioServiceHandler._pausedTracks"/> 的 remarks；改成基类懒分配即判失败）。
    /// </remarks>
    /// <remarks>
    /// 中间件一侧用本文件的本地夹具（<see cref="PauseFlagHandler"/> / <see cref="PauseFlagBridge"/>）：FMOD / Wwise 后端已外置到插件装配， <br />
    /// 本用例锁的是框架契约基类的暂停标记，断言与期望值一字未动。
    /// </remarks>
    [TestFixture]
    public sealed class AudioTrackPauseFlagTests
    {
        /// <summary>能出声的本地桥：每次播放给一个自增实例 ID，与被外置的 Stub 同一口径。</summary>
        private sealed class PauseFlagBridge : IAudioMiddlewareBridge
        {
            private ulong _nextId = 1UL;

            public bool Initialize(UnityEngine.Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }

            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, UnityEngine.Vector3? position3D)
                => string.IsNullOrEmpty(eventPath) ? 0UL : _nextId++;

            public void StopInstance(ulong instanceId) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }
            public void SetBusVolume(string busPath, float volume) { }
            public float GetBusVolume(string busPath) => 1f;
            public bool IsPlaying(ulong instanceId) => true;
            public string GetEventPathFromClip(AudioClip clip)
                => clip == null || string.IsNullOrEmpty(clip.name) ? null : "event:/" + clip.name;
        }

        /// <summary>中间件后端的本地替身：只提供抽象的默认桥工厂，暂停标记语义全在框架契约里。</summary>
        private sealed class PauseFlagHandler : MiddlewareAudioHandler
        {
            protected override IAudioMiddlewareBridge CreateDefaultBridge() => new PauseFlagBridge();
        }

        private AudioClip _clip;
        private PauseFlagHandler _middleware;

        [SetUp]
        public void SetUp()
        {
            _clip = AudioClip.Create("PauseFlagProbe", 1, 1, 44100, false);
            _middleware = new PauseFlagHandler();
            _middleware.SetBridge(new PauseFlagBridge());
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
