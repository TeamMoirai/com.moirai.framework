using System.Collections;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using Moirai.Atropos.Audio.Middleware;
using Moirai.Atropos.Audio.Wwise;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 中间件 / 混音快照 / 遮挡 契约与集成测试。
    /// </summary>
    [TestFixture]
    public sealed class AudioMiddlewareMixPlayModeTests
    {
        [UnityTest]
        public IEnumerator FmodAndWwise_ShareMiddlewareContract()
        {
            var fmodStub = new FmodBridgeStub();
            var fmod = new FmodAudioHandler();
            fmod.SetBridge(fmodStub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(fmod, null);

            var wwiseStub = new WwiseBridgeStub();
            var wwise = new WwiseAudioHandler();
            wwise.SetBridge(wwiseStub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(wwise, null);

            var request = new AudioPlayRequest(1, 1f, 1f, EAudioTrack.Sfx, 128, AudioPlayFlags.Loop);
            ulong h1 = fmod.Play("event:/Test", request, null);
            ulong h2 = wwise.Play("wwise:/Test", new AudioPlayRequest(2, 1f, 1f, EAudioTrack.Sfx, 128, AudioPlayFlags.Loop), null);

            Assert.AreNotEqual(0UL, h1);
            Assert.AreNotEqual(0UL, h2);
            Assert.IsTrue(fmod.IsPlaying(h1));
            Assert.IsTrue(wwise.IsPlaying(h2));

            fmod.StopByID(1, 0f);
            wwise.StopByID(2, 0f);
            yield return null;

            Assert.IsTrue(fmod.IsStopped(h1));
            Assert.IsTrue(wwise.IsStopped(h2));
        }

        [Test]
        public void MixStateMachine_PriorityBlocksLowerInterrupt()
        {
            var machine = new AudioMixStateMachine();
            // Cinematic priority 默认 4 > Dialogue 3
            Assert.IsTrue(machine.Request(EMixSnapshot.Cinematic));
            Assert.AreEqual(EMixSnapshot.Cinematic, machine.Current);

            // 低优先级不可打断
            Assert.IsFalse(machine.Request(EMixSnapshot.Default));
            Assert.AreEqual(EMixSnapshot.Cinematic, machine.Current);

            // force 可打断
            Assert.IsTrue(machine.Request(EMixSnapshot.Default, force: true));
            Assert.AreEqual(EMixSnapshot.Default, machine.Current);
        }

        [Test]
        public void MixStateMachine_SameOrHigherPrioritySwitches()
        {
            var machine = new AudioMixStateMachine();
            Assert.IsTrue(machine.Request(EMixSnapshot.Dialogue));
            Assert.IsTrue(machine.Request(EMixSnapshot.Paused)); // 同级 3
            Assert.AreEqual(EMixSnapshot.Paused, machine.Current);
            Assert.IsFalse(machine.Request(EMixSnapshot.Muffled)); // 2 < 3，不可打断
            Assert.AreEqual(EMixSnapshot.Paused, machine.Current);
        }

        [Test]
        public void MixStateMachine_MiddlewareHandlerInvoked()
        {
            var machine = new AudioMixStateMachine();
            EMixSnapshot seen = EMixSnapshot.Default;
            float blend = -1f;
            machine.SetMiddlewareTransitionHandler((s, b) =>
            {
                seen = s;
                blend = b;
            });

            Assert.IsTrue(machine.Request(EMixSnapshot.Muffled, 0.2f));
            Assert.AreEqual(EMixSnapshot.Muffled, seen);
            Assert.AreEqual(0.2f, blend, 1e-4f);
        }

        [UnityTest]
        public IEnumerator OcclusionRegistry_RegisterUnregister()
        {
            var go = new GameObject("OccTest");
            var occ = go.AddComponent<AudioOcclusionHrtf>();
            yield return null;

            Assert.AreSame(occ, AudioOcclusionRegistry.Active);

            Object.Destroy(go);
            yield return null;
            // Destroy 后 OnDisable 应清空
            Assert.IsTrue(AudioOcclusionRegistry.Active == null || AudioOcclusionRegistry.Active != occ);
        }

        [UnityTest]
        public IEnumerator Middleware_StopByID_LayerIsolation()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(handler, null);

            ulong a = handler.Play("event:/M/A", new AudioPlayRequest(10, 1f, 1f, EAudioTrack.Music, 128, AudioPlayFlags.Loop), null);
            ulong b = handler.Play("event:/M/B", new AudioPlayRequest(11, 1f, 1f, EAudioTrack.Music, 128, AudioPlayFlags.Loop), null);
            yield return null;

            handler.StopByID(10, 0f);
            yield return null;

            Assert.IsTrue(handler.IsStopped(a));
            Assert.IsTrue(handler.IsPlaying(b));
            handler.StopAll(0f);
        }
    }
}
