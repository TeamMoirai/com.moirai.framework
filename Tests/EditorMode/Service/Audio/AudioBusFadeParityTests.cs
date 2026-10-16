using Moirai.Atropos;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// 总线过渡对拍：Master / 音轨淡入淡出由 <see cref="AudioServiceHandler"/> 契约实现。
    /// </summary>
    /// <remarks>
    /// 两个后端只在 <see cref="IAudioFadeTarget.ApplyFade"/> 的落点上分岔，用例量的就是这个落点。 <br />
    /// 不经 <c>Tick</c> 推进，而是按给定时刻直推 internal 过渡表（测试程序集在 <c>InternalsVisibleTo</c> 白名单内），
    /// 故结论不依赖 <c>GameTime.unscaledTime</c> 是否被驱动。 <br />
    /// 中间件一侧用本文件的本地夹具（<see cref="ParityHandler"/> / <see cref="ParityBridge"/>）：FMOD / Wwise 后端已外置到插件装配， <br />
    /// 本用例锁的是框架契约基类的过渡语义，与具体 SDK 无关，故夹具留在框架、断言与期望值一字未动。
    /// </remarks>
    [TestFixture]
    public sealed class AudioBusFadeParityTests
    {
        /// <summary>记录总线音量的本地桥——写入夹取一次、未写过读作 0，与被外置的 Stub 同一口径。</summary>
        private sealed class ParityBridge : IAudioMiddlewareBridge
        {
            private readonly System.Collections.Generic.Dictionary<string, float> _busVolumes =
                new System.Collections.Generic.Dictionary<string, float>(8);
            private ulong _nextId = 1UL;

            public float GetBusVolume(string busPath)
                => _busVolumes.TryGetValue(busPath, out float v) ? v : 0f;

            public void SetBusVolume(string busPath, float volume)
            {
                if (!string.IsNullOrEmpty(busPath)) _busVolumes[busPath] = Mathf.Clamp01(volume);
            }

            public bool Initialize(Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }

            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D)
                => string.IsNullOrEmpty(eventPath) ? 0UL : _nextId++;

            public void StopInstance(ulong instanceId) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }
            public bool IsPlaying(ulong instanceId) => true;
            public string GetEventPathFromClip(AudioClip clip)
                => clip == null || string.IsNullOrEmpty(clip.name) ? null : "event:/" + clip.name;
        }

        /// <summary>中间件后端的本地替身：契约基类的过渡与总线语义全在框架，这里只补上抽象的默认桥工厂。</summary>
        private sealed class ParityHandler : MiddlewareAudioHandler
        {
            protected override IAudioMiddlewareBridge CreateDefaultBridge() => new ParityBridge();
        }

        private const float Duration = 1f;

        private float _listenerVolumeBefore;

        [SetUp]
        public void SetUp()
        {
            // Unity 侧 MasterVolume 的落点是 AudioListener.volume（进程级），不复原会串进同域后续用例
            _listenerVolumeBefore = AudioListener.volume;
        }

        [TearDown]
        public void TearDown() => AudioListener.volume = _listenerVolumeBefore;

        [Test]
        public void ZeroDurationFade_AppliesAtOnce_AndSchedulesNothing_OnBothBackends()
        {
            var unity = new UnityAudioHandler();
            unity.FadeMasterTrack(0f, 0f, 0.7f);

            Assert.AreEqual(0.7f, unity.MasterVolume, 1e-4f, "时长 0 必须当场赋值，不排过渡");
            Assert.IsFalse(unity._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE),
                "时长 0 排了过渡——淡出会再走一遍并把值拉回起点");

            var stub = new ParityBridge();
            var middleware = new ParityHandler();
            middleware.SetBridge(stub);
            middleware.FadeMasterTrack(0f, 0f, 0.7f);

            Assert.AreEqual(0.7f, middleware.MasterVolume, 1e-4f);
            Assert.AreEqual(0.7f, stub.GetBusVolume("bus:/Master"), 1e-4f, "零时长也要落到总线上");
            Assert.IsFalse(middleware._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE));
        }

        [Test]
        public void MasterFade_ReachesTarget_AndFreesTheSlot_OnBothBackends()
        {
            float start = GameTime.unscaledTime;

            var unity = new UnityAudioHandler();
            unity.FadeMasterTrack(Duration, 0f, 1f);
            Assert.IsTrue(unity._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE), "过渡没排上");

            unity._fades.Update(start + Duration * 0.5f, unity);
            Assert.AreEqual(0.5f, unity.MasterVolume, 0.02f, "中点应在线性过渡的一半");

            unity._fades.Update(start + Duration, unity);
            Assert.AreEqual(1f, unity.MasterVolume, 1e-4f, "到点必须写终值而不是曲线值");
            Assert.IsFalse(unity._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE), "完成后没让位");

            var stub = new ParityBridge();
            var middleware = new ParityHandler();
            middleware.SetBridge(stub);
            middleware.FadeMasterTrack(Duration, 0f, 1f);
            middleware._fades.Update(start + Duration * 0.5f, middleware.Internal_PeekFadeTarget());

            Assert.AreEqual(0.5f, middleware.MasterVolume, 0.02f, "中点与 Unity 侧不同值");
            Assert.AreEqual(0.5f, stub.GetBusVolume("bus:/Master"), 0.02f, "getter 报回的数必须就是总线上的数");

            middleware._fades.Update(start + Duration, middleware.Internal_PeekFadeTarget());
            Assert.AreEqual(1f, stub.GetBusVolume("bus:/Master"), 1e-4f);
            Assert.IsFalse(middleware._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE));
        }

        [Test]
        public void StopFade_KeepsWhereItStopped_OnBothBackends()
        {
            float start = GameTime.unscaledTime;

            var unity = new UnityAudioHandler();
            unity.FadeMasterTrack(Duration, 1f, 0f);
            unity._fades.Update(start + Duration * 0.25f, unity);
            float stoppedAt = unity.MasterVolume;
            unity.StopFadeMasterTrack();

            Assert.IsFalse(unity._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE));
            Assert.AreEqual(stoppedAt, unity.MasterVolume, 1e-4f, "撤过渡不得还原已写出去的音量——停在哪儿就是哪儿");

            var stub = new ParityBridge();
            var middleware = new ParityHandler();
            middleware.SetBridge(stub);
            middleware.FadeMasterTrack(Duration, 1f, 0f);
            middleware._fades.Update(start + Duration * 0.25f, middleware.Internal_PeekFadeTarget());
            stoppedAt = middleware.MasterVolume;
            middleware.StopFadeMasterTrack();

            Assert.IsFalse(middleware._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE));
            Assert.AreEqual(stoppedAt, stub.GetBusVolume("bus:/Master"), 1e-4f, "中间件侧停在同一处");
        }

        [Test]
        public void RestartingAFade_DoesNotStack_OnBothBackends()
        {
            float start = GameTime.unscaledTime;

            var unity = new UnityAudioHandler();
            unity.FadeMasterTrack(Duration, 0f, 1f);
            unity.FadeMasterTrack(Duration, 0.2f, 0.6f);
            Assert.AreEqual(1, unity._fades.Count, "同一条总线排了两条过渡——后一次没顶掉前一次");
            unity._fades.Update(start + Duration, unity);
            Assert.AreEqual(0.6f, unity.MasterVolume, 1e-4f, "终值应来自后一次请求");

            var stub = new ParityBridge();
            var middleware = new ParityHandler();
            middleware.SetBridge(stub);
            middleware.FadeMasterTrack(Duration, 0f, 1f);
            middleware.FadeMasterTrack(Duration, 0.2f, 0.6f);
            Assert.AreEqual(1, middleware._fades.Count);
            middleware._fades.Update(start + Duration, middleware.Internal_PeekFadeTarget());
            Assert.AreEqual(0.6f, stub.GetBusVolume("bus:/Master"), 1e-4f);
        }

        [Test]
        public void TrackFades_AreIndependentPerTrack_AndLandOnTheirOwnBus()
        {
            float start = GameTime.unscaledTime;
            var stub = new ParityBridge();
            var middleware = new ParityHandler();
            middleware.SetBridge(stub);

            middleware.FadeTrack(EAudioTrack.Music, Duration, 1f, 0f);
            middleware.FadeTrack(EAudioTrack.Ambience, Duration, 0f, 1f);
            Assert.AreEqual(2, middleware._fades.Count, "两条音轨过渡挤成了同一条");

            middleware._fades.Update(start + Duration, middleware.Internal_PeekFadeTarget());

            Assert.AreEqual(0f, stub.GetBusVolume("bus:/Music"), 1e-4f, "Music 淡出没走完");
            Assert.AreEqual(1f, stub.GetBusVolume("bus:/Ambience"), 1e-4f, "Ambience 淡入没走完");
            Assert.AreEqual(0, middleware._fades.Count);

            middleware.StopFadeTrack(EAudioTrack.Music);
            middleware.StopFadeTrack(EAudioTrack.Ambience);
            Assert.IsFalse(middleware._fades.IsFading(AudioFadeScheduler.TrackFadeHandle((int)EAudioTrack.Music)));
        }

        [Test]
        public void FadeTable_IsPerInstance_NotSharedAcrossHandlers()
        {
            // 调度器现在是契约基类上的字段：一旦被改成 static，两个后端会互顶对方的过渡，
            // 而"编辑器里同时挂着 Unity 与中间件"正是热切换后端时的常态。
            var first = new ParityHandler();
            first.SetBridge(new ParityBridge());
            var second = new ParityHandler();
            second.SetBridge(new ParityBridge());

            first.FadeMasterTrack(Duration, 0f, 1f);

            Assert.AreEqual(1, first._fades.Count);
            Assert.AreEqual(0, second._fades.Count, "过渡表跨实例共享了");

            second.StopFadeMasterTrack();
            Assert.IsTrue(first._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE),
                "别的处理器撤不掉本处理器的过渡");
        }
    }
}
