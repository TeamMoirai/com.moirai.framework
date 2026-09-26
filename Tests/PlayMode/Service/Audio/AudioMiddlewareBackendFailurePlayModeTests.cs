using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 中间件后端初始化失败的降级面（上线门槛 G5「SDK 初始化失败」）：
    /// 失败即整体禁用，不得再有任何一次调用打到未初始化的原生引擎。
    /// </summary>
    [TestFixture]
    public sealed class AudioMiddlewareBackendFailurePlayModeTests
    {
        /// <summary>Initialize 返回 false 的假桥，并统计失败后被触达的次数。</summary>
        private sealed class DeadBridge : IAudioMiddlewareBridge, IAudioMiddlewareBankControl, IAudioMiddlewareRtpcControl
        {
            public int InitializeCalls;
            public readonly Dictionary<string, int> Touched = new Dictionary<string, int>(StringComparer.Ordinal);

            public bool Initialize(Transform instanceRoot)
            {
                InitializeCalls++;
                return false;
            }

            public void Shutdown() => Touch(nameof(Shutdown));
            public void Update(float unscaledDeltaTime) => Touch(nameof(Update));

            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D)
            {
                Touch(nameof(PlayEvent));
                return 42UL;
            }

            public void StopInstance(ulong instanceId, bool immediate) => Touch(nameof(StopInstance));
            public void SetPaused(ulong instanceId, bool paused) => Touch(nameof(SetPaused));
            public void SetInstanceVolume(ulong instanceId, float volume) => Touch(nameof(SetInstanceVolume));
            public void SetBusVolume(string busPath, float volume) => Touch(nameof(SetBusVolume));
            public float GetBusVolume(string busPath)
            {
                Touch(nameof(GetBusVolume));
                return 1f;
            }

            public bool IsPlaying(ulong instanceId)
            {
                Touch(nameof(IsPlaying));
                return true;
            }

            public string GetEventPathFromClip(AudioClip clip)
            {
                Touch(nameof(GetEventPathFromClip));
                return "event:/Dead";
            }

            public EAudioBankLoadResult LoadBank(string bankPath)
            {
                Touch(nameof(LoadBank));
                return EAudioBankLoadResult.Loaded;
            }

            public bool UnloadBank(string bankPath)
            {
                Touch(nameof(UnloadBank));
                return true;
            }

            public void SetRtpc(string name, float value, ulong instanceId) => Touch(nameof(SetRtpc));

            private void Touch(string member)
            {
                Touched.TryGetValue(member, out int count);
                Touched[member] = count + 1;
            }
        }

        private static void Invoke(MiddlewareAudioHandler handler, string member)
        {
            typeof(MiddlewareAudioHandler)
                .GetMethod(member, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(handler, null);
        }

        [Test]
        public void Init_Failed_BridgeIsDroppedAndAudioDisabled()
        {
            var dead = new DeadBridge();
            var handler = new FmodAudioHandler();
            handler.SetBridge(dead);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("桥接初始化失败"));
            Invoke(handler, "OnInit");
            Transform root = handler.InstanceRoot;

            Assert.AreEqual(1, dead.InitializeCalls);
            Assert.IsNull(handler.Bridge, "初始化失败后不得留下半初始化的桥——保留就是让后续每次调用打到未初始化的原生层");

            Assert.AreEqual(0UL, handler.Play("event:/Anything",
                new AudioPlayRequest(1, 1f, 1f, EAudioTrack.Sfx, 128, EAudioPlayFlags.None), null));
            Assert.IsFalse(handler.LoadBank("Master"));
            Assert.IsFalse(handler.UnloadBank("Master"));
            handler.SetRtpc("Health", 0.5f, 0UL);
            handler.MasterVolume = 0.5f;
            handler.SetTrackVolume(EAudioTrack.Music, 0.5f);
            handler.LoadMasterSettings();
            handler.StopAll(0f);
            handler.Restart();
            Invoke(handler, "OnShutdown");

            Assert.IsEmpty(dead.Touched,
                "除 Initialize 外不得再触达任何桥接成员，实际触达：" + string.Join(", ", dead.Touched.Keys));

            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        }

        [UnityTest]
        public IEnumerator Init_Failed_TickStaysQuietAcrossFrames()
        {
            var dead = new DeadBridge();
            var handler = new FmodAudioHandler();
            handler.SetBridge(dead);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("桥接初始化失败"));
            Invoke(handler, "OnInit");

            Transform root = handler.InstanceRoot;
            for (int frame = 0; frame < 3; frame++)
            {
                yield return null;
                handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);
            }

            Assert.IsEmpty(dead.Touched, "Tick 每帧驱动桥接与回收，禁用态下必须整体静默");

            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        }

        /// <summary>
        /// 禁用态下的音量面：读作 0、写作无效、且不排总线过渡。
        /// <para>补这一件是因为"引擎已死"与"音量是 80%"此前可以同时成立——设置面板照旧显示音量、
        /// 照旧接受拖动，而玩家什么也听不见；这正是线上无法归因的那类症状。
        /// 契约第 6 条（<c>IsBackendInert</c>）把它定成两后端共同的口径，Unity 侧一直就是这么做的。</para>
        /// </summary>
        [Test]
        public void Init_Failed_VolumeSurfaceReadsZeroAndIgnoresWrites()
        {
            var dead = new DeadBridge();
            var handler = new FmodAudioHandler();
            handler.SetBridge(dead);

            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("桥接初始化失败"));
            Invoke(handler, "OnInit");
            Transform root = handler.InstanceRoot;

            Assert.AreEqual(0f, handler.MasterVolume, "引擎已死却报着一个音量，就是无法归因的那种假象");
            Assert.AreEqual(0f, handler.GetTrackVolume(EAudioTrack.Music));
            Assert.IsFalse(handler.GetTrackMute(EAudioTrack.Music));

            handler.MasterVolume = 0.8f;
            handler.SetTrackVolume(EAudioTrack.Music, 0.8f);
            Assert.AreEqual(0f, handler.MasterVolume, "禁用态的写入不得留下「写过一次」的状态");
            Assert.AreEqual(0f, handler.GetTrackVolume(EAudioTrack.Music));

            // 总线过渡由契约实现：inert 时不该排程，否则 SoundIsFadingOut 会报真而实际无声
            handler.FadeMasterTrack(2f, 0f, 1f);
            handler.FadeTrack(EAudioTrack.Music, 2f, 0f, 1f);
            Assert.IsFalse(handler._fades.IsFading(AudioFadeScheduler.MASTER_FADE_HANDLE));
            Assert.IsFalse(handler._fades.IsFading(AudioFadeScheduler.TrackFadeHandle((int)EAudioTrack.Music)));
            Assert.IsFalse(handler.SoundIsFadingOut(AudioFadeScheduler.MASTER_FADE_HANDLE));

            Assert.IsEmpty(dead.Touched, "音量面转 inert 后仍不得触达原生桥：" + string.Join(", ", dead.Touched.Keys));

            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
