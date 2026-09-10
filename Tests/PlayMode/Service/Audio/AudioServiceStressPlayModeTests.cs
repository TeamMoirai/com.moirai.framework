using System.Collections;
using System.Collections.Generic;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// Audio 并发压测：高频 Play/Stop/Fade/分层替换，验证句柄不泄漏、字典不膨胀。
    /// </summary>
    [TestFixture]
    public sealed class AudioServiceStressPlayModeTests
    {
        private GameObject _root;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("[AudioStress]");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<AudioListener>();

            _clip = AudioClip.Create("stress_tone", 44100 * 2, 1, 44100, false);
            _clip.SetData(new float[44100 * 2], 0);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            if (_clip != null) Object.Destroy(_clip);
            _root = null;
            _clip = null;
        }

        [UnityTest]
        public IEnumerator UnityHandler_BurstPlayStop_HandleMapDoesNotGrowUnbounded()
        {
            var handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(handler, null);

            if (handler.AudioCategories == null || handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过 Unity 压测");
            }

            const int BURST = 200;
            var handles = new List<ulong>(BURST);

            for (int i = 0; i < BURST; i++)
            {
                var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
                options.ID = 10000 + (i % 8);
                options.DoNotAutoRecycleIfNotDonePlaying = false;
                ulong h = handler.Play(_clip, options);
                if (h != 0UL) handles.Add(h);

                // 交错停止一半
                if (handles.Count > 0 && (i & 1) == 1)
                {
                    handler.Stop(handles[handles.Count - 1], 0f);
                    handles.RemoveAt(handles.Count - 1);
                }
            }

            yield return null;

            // 清空剩余
            for (int i = 0; i < handles.Count; i++)
            {
                handler.Stop(handles[i], 0f);
            }

            yield return null;
            handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);
            yield return null;
            handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);

            // 结束后所有句柄应已释放
            int leaked = 0;
            for (int i = 0; i < handles.Count; i++)
            {
                if (handler.GetAgentByHandle(handles[i]) != null) leaked++;
            }

            Assert.AreEqual(0, leaked, "Stop 后句柄映射必须清空");

            handler.StopAll(0f);
        }

        [UnityTest]
        public IEnumerator UnityHandler_ConcurrentFadeAndStop_NoException()
        {
            var handler = new UnityAudioHandler();
            var init = typeof(UnityAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(handler, null);

            if (handler.AudioCategories == null || handler.AudioCategories.Length == 0)
            {
                Assert.Ignore("AudioGroupConfigs 未配置，跳过");
            }

            var handles = new List<ulong>(32);
            for (int i = 0; i < 32; i++)
            {
                var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
                options.ID = 20000 + i % 4;
                // 允许抢占，避免 MaxChannel 耗尽后整批失败
                options.DoNotAutoRecycleIfNotDonePlaying = false;
                var h = handler.Play(_clip, options);
                if (h != 0UL) handles.Add(h);
            }

            yield return null;
            handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);

            // 同时 Fade 多个 + 穿插 Stop
            for (int i = 0; i < handles.Count; i++)
            {
                handler.FadeAudio(handles[i], 0.2f, 1f, 0f, default);
            }

            handler.PlayFadeByID(20000, 0.15f, 0.1f, default);
            handler.StopByID(20001, 0.05f);

            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.unscaledDeltaTime;
                // 每帧 Tick 由服务驱动；这里手动压 handler
                handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.DoesNotThrow(() => handler.StopAll(0.05f));
            yield return null;
            handler.StopAll(0f);
        }

        [UnityTest]
        public IEnumerator FmodHandler_BurstPlayStop_StubBridge_HandlesStayConsistent()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);

            var init = typeof(FmodAudioHandler).BaseType
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            init.Invoke(handler, null);

            Assert.IsNotNull(handler.Bridge);
            Assert.IsTrue(stub.PlayCount >= 0);

            const int BURST = 150;
            var handles = new List<ulong>(BURST);

            for (int i = 0; i < BURST; i++)
            {
                var request = new AudioPlayRequest(50000 + (i % 5), 1f, 1f, EAudioTrack.Sfx, 128,
                    AudioPlayFlags.DoNotAutoRecycle);
                ulong h = handler.Play("event:/Stress/Hit", request, null);
                if (h != 0UL) handles.Add(h);

                if (handles.Count > 10 && (i % 3) == 0)
                {
                    handler.Stop(handles[0], 0f);
                    handles.RemoveAt(0);
                }
            }

            yield return null;
            handler.Tick(0.016f, 0.016f);
            yield return null;

            handler.StopAll(0f);
            yield return null;
            handler.Tick(0.016f, 0.016f);

            for (int i = 0; i < handles.Count; i++)
            {
                Assert.IsTrue(handler.IsStopped(handles[i]), "StopAll 后句柄应释放: " + handles[i]);
            }

            Assert.Greater(stub.PlayCount, 0, "Stub 应记录 Play 次数");
        }

        [UnityTest]
        public IEnumerator FmodHandler_MultiIdLayering_StopByIDIsolated()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(handler, null);

            ulong a = handler.Play("event:/Music/Theme", new AudioPlayRequest(1, 1f, 1f, EAudioTrack.Music, 128, AudioPlayFlags.Loop), null);
            ulong b = handler.Play("event:/Music/Ambience", new AudioPlayRequest(2, 1f, 1f, EAudioTrack.Music, 128, AudioPlayFlags.Loop), null);
            yield return null;

            Assert.IsTrue(handler.IsPlaying(a));
            Assert.IsTrue(handler.IsPlaying(b));

            handler.StopByID(1, 0f);
            yield return null;

            Assert.IsTrue(handler.IsStopped(a), "ID1 应停");
            Assert.IsTrue(handler.IsPlaying(b), "ID2 不受影响");

            handler.StopByID(2, 0f);
            yield return null;
            Assert.IsTrue(handler.IsStopped(b));
        }

        [UnityTest]
        public IEnumerator FmodHandler_FadeCompletes_AppliesEase()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(handler, null);

            ulong h = handler.Play("event:/Sfx/Beep",
                new AudioPlayRequest(9, 1f, 1f, EAudioTrack.Sfx, 128, AudioPlayFlags.None), null);
            Assert.AreNotEqual(0UL, h);

            handler.FadeAudio(h, 0.12f, 1f, 0.2f, new TweenEase(TweenUtility.EEase.Linear));
            Assert.IsTrue(handler.SoundIsFadingOut(h));

            float elapsed = 0f;
            while (elapsed < 0.4f && handler.SoundIsFadingOut(h))
            {
                elapsed += Time.unscaledDeltaTime;
                handler.Tick(Time.unscaledDeltaTime, Time.unscaledDeltaTime);
                yield return null;
            }

            Assert.IsFalse(handler.SoundIsFadingOut(h));
            handler.Stop(h, 0f);
        }

        [UnityTest]
        public IEnumerator HostPool_AcquireRelease_ReusesInstances()
        {
            var parent = new GameObject("PoolParent").transform;
            parent.SetParent(_root.transform);

            AudioAgentHostPool.Warmup(parent, 2);
            int before = AudioAgentHostPool.StackCount;

            var a = AudioAgentHostPool.Acquire(parent, "H0");
            var b = AudioAgentHostPool.Acquire(parent, "H1");
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.AreNotSame(a, b);

            AudioAgentHostPool.Release(a);
            AudioAgentHostPool.Release(b);

            var c = AudioAgentHostPool.Acquire(parent, "H2");
            // 应优先复用栈中实例
            Assert.IsTrue(c == a || c == b, "归还后应复用宿主");

            AudioAgentHostPool.Release(c);
            AudioAgentHostPool.Clear();
            yield return null;
        }
    }
}
