using System.Collections;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Middleware;
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
        /// <summary>记录播放的本地桥——口径与被外置的 <c>*BridgeStub</c> 一致，供框架侧的混音/分层用例观测。</summary>
        private sealed class MixBridge : IAudioMiddlewareBridge
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
            public string GetEventPathFromClip(UnityEngine.AudioClip clip)
                => clip == null || string.IsNullOrEmpty(clip.name) ? null : "event:/" + clip.name;
        }

        /// <summary>中间件后端的本地替身：只提供抽象的默认桥工厂，混音与分层语义全在框架契约基类里。</summary>
        private sealed class MixHandler : MiddlewareAudioHandler
        {
            protected override IAudioMiddlewareBridge CreateDefaultBridge() => new MixBridge();
        }

        /// <summary>
        /// 按名取插件装配里的后端 Handler；插件没装时返回 null。
        /// </summary>
        /// <remarks>框架测试装配不能引用插件装配（那会让框架反向依赖可选插件），两支共享契约这条只能按名探；探不到就跳过而不是判过。</remarks>
        private static MiddlewareAudioHandler CreatePluginHandler(string typeFullName, string assemblyName)
        {
            System.Type handlerType = System.Type.GetType(typeFullName + ", " + assemblyName, throwOnError: false);
            if (handlerType == null) return null;
            return (MiddlewareAudioHandler)System.Activator.CreateInstance(handlerType, true);
        }

        /// <summary>按名取插件装配里的 Stub 桥（两支 Stub 都实现了主桥接口，公共服务面即可驱动）。</summary>
        private static IAudioMiddlewareBridge CreatePluginStubBridge(string typeFullName, string assemblyName)
        {
            System.Type bridgeType = System.Type.GetType(typeFullName + ", " + assemblyName, throwOnError: false);
            if (bridgeType == null) return null;
            return (IAudioMiddlewareBridge)System.Activator.CreateInstance(bridgeType, true);
        }

        [UnityTest]
        public IEnumerator FmodAndWwise_ShareMiddlewareContract()
        {
            var fmodStub = CreatePluginStubBridge("Moirai.Atropos.Audio.Fmod.FmodBridgeStub", "Moirai.Plugin.Fmod");
            var fmod = CreatePluginHandler("Moirai.Atropos.Audio.Fmod.FmodAudioHandler", "Moirai.Plugin.Fmod");

            var wwiseStub = CreatePluginStubBridge("Moirai.Atropos.Audio.Wwise.WwiseBridgeStub", "Moirai.Plugin.Wwise");
            var wwise = CreatePluginHandler("Moirai.Atropos.Audio.Wwise.WwiseAudioHandler", "Moirai.Plugin.Wwise");

            if (fmod == null || wwise == null)
            {
                Assert.Ignore("fmod / wwise 插件装配没同时装上，两支共享中间件契约这条无从判起");
                yield break;
            }

            fmod.SetBridge(fmodStub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(fmod, null);

            wwise.SetBridge(wwiseStub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(wwise, null);

            var request = new AudioPlayRequest(1, 1f, 1f, EAudioTrack.Sfx, 128, EAudioPlayFlags.Loop);
            ulong h1 = fmod.Play("event:/Test", request, null);
            ulong h2 = wwise.Play("wwise:/Test", new AudioPlayRequest(2, 1f, 1f, EAudioTrack.Sfx, 128, EAudioPlayFlags.Loop), null);

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

        /// <summary>
        /// 建一台可施加的混音状态机：挂上生产就有的中间件过渡接缝。
        /// </summary>
        /// <remarks>
        /// <see cref="AudioMixStateMachine.Request"/> 只在过渡真的施加到混音上时才返回 true， <br />
        /// 既无 Mixer 又无施加通道一律拒绝并保持 <see cref="AudioMixStateMachine.Current"/> 不变； <br />
        /// 故优先级 / 回落语义要可观测，必须挂该接缝（与 EditorMode 的 <c>AudioMixStateMachineTests</c> 同一口径）。
        /// </remarks>
        private static AudioMixStateMachine CreateMixMachine()
        {
            var machine = new AudioMixStateMachine();
            machine.SetMiddlewareTransitionHandler((state, blendSeconds) => { });
            return machine;
        }

        [Test]
        public void MixStateMachine_PriorityBlocksLowerInterrupt()
        {
            var machine = CreateMixMachine();
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
            var machine = CreateMixMachine();
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
            var stub = new MixBridge();
            var handler = new MixHandler();
            handler.SetBridge(stub);
            typeof(MiddlewareAudioHandler)
                .GetMethod("OnInit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(handler, null);

            ulong a = handler.Play("event:/M/A", new AudioPlayRequest(10, 1f, 1f, EAudioTrack.Music, 128, EAudioPlayFlags.Loop), null);
            ulong b = handler.Play("event:/M/B", new AudioPlayRequest(11, 1f, 1f, EAudioTrack.Music, 128, EAudioPlayFlags.Loop), null);
            yield return null;

            handler.StopByID(10, 0f);
            yield return null;

            Assert.IsTrue(handler.IsStopped(a));
            Assert.IsTrue(handler.IsPlaying(b));
            handler.StopAll(0f);
        }
    }
}
