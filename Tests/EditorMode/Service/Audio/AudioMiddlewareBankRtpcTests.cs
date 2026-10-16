using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Middleware;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 中间件 Bank / RTPC 能力契约：Handler 外观派发、能力探测降级与加载失败可归因。
    /// </summary>
    /// <remarks>
    /// 本文件只留「与具体 SDK 无关」的框架契约面，夹具用本地的 <see cref="ContractBridge"/> / <see cref="ContractHandler"/>； <br />
    /// FMOD / Wwise 两个插件装配（<c>com.moirai.framework.plugin</c> 的 <c>fmod/</c>、<c>wwise/</c>）里的 <c>*BridgeStub</c> 自带语义， <br />
    /// 它们的用例已随插件走：<c>fmod/Tests/AudioFmodBankRtpcTests.cs</c> 与 <c>wwise/Tests/AudioWwiseBankRtpcTests.cs</c>（断言与期望值未改）。 <br />
    /// 不依赖 <c>FMOD_INSTALLED</c> / <c>WWISE_INSTALLED</c>，CI 无插件也能跑。
    /// </remarks>
    [TestFixture]
    public sealed class AudioMiddlewareBankRtpcTests
    {
        #region 本地夹具 [LOCAL FIXTURES]

        /// <summary>
        /// 仅实现主桥接口的假件——验证能力探测安全降级，也是能力子类化的底座。
        /// </summary>
        private class BridgeWithoutCapabilities : IAudioMiddlewareBridge
        {
            public bool Initialize(UnityEngine.Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }
            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, UnityEngine.Vector3? position3D) => 0UL;
            public void StopInstance(ulong instanceId) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }
            public void SetBusVolume(string busPath, float volume) { }
            public float GetBusVolume(string busPath) => 0f;
            public bool IsPlaying(ulong instanceId) => false;
            public string GetEventPathFromClip(UnityEngine.AudioClip clip) => null;
        }

        /// <summary>
        /// 可编排加载结果、并统计触达次数的假桥。
        /// </summary>
        private sealed class ControllableBankBridge : BridgeWithoutCapabilities, IAudioMiddlewareBankControl
        {
            public EAudioBankLoadResult NextResult = EAudioBankLoadResult.Loaded;
            public int LoadCalls;

            public EAudioBankLoadResult LoadBank(string bankPath)
            {
                LoadCalls++;
                return NextResult;
            }

            public bool UnloadBank(string bankPath) => true;
        }

        /// <summary>
        /// 记账用的本地桥——声音库与实时参数的登记口径与被外置的 <c>*BridgeStub</c> 一致，供派发用例观测。
        /// </summary>
        private sealed class ContractBridge : IAudioMiddlewareBridge, IAudioMiddlewareBankControl, IAudioMiddlewareRtpcControl
        {
            private ulong _nextId = 1UL;

            /// <summary>已加载的声音库。</summary>
            public readonly System.Collections.Generic.HashSet<string> Banks =
                new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

            /// <summary>已设置的参数值；全局作用域按参数名，实例作用域带 "实例ID:" 前缀。</summary>
            public readonly System.Collections.Generic.Dictionary<string, float> RtpcValues =
                new System.Collections.Generic.Dictionary<string, float>(System.StringComparer.Ordinal);

            public int BankLoadCount { get; private set; }
            public int RtpcCount { get; private set; }

            public bool Initialize(UnityEngine.Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }

            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, UnityEngine.Vector3? position3D)
                => string.IsNullOrEmpty(eventPath) ? 0UL : _nextId++;

            public void StopInstance(ulong instanceId) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }
            public void SetBusVolume(string busPath, float volume) { }
            public float GetBusVolume(string busPath) => 0f;
            public bool IsPlaying(ulong instanceId) => true;
            public string GetEventPathFromClip(UnityEngine.AudioClip clip)
                => clip == null || string.IsNullOrEmpty(clip.name) ? null : "event:/" + clip.name;

            public EAudioBankLoadResult LoadBank(string bankPath)
            {
                if (string.IsNullOrEmpty(bankPath)) return EAudioBankLoadResult.Failed;
                if (!Banks.Add(bankPath)) return EAudioBankLoadResult.AlreadyLoaded;

                BankLoadCount++;
                return EAudioBankLoadResult.Loaded;
            }

            public bool UnloadBank(string bankPath)
                => !string.IsNullOrEmpty(bankPath) && Banks.Remove(bankPath);

            public void SetRtpc(string name, float value, ulong instanceId)
            {
                if (string.IsNullOrEmpty(name)) return;
                RtpcValues[instanceId == 0UL ? name : instanceId + ":" + name] = value;
                RtpcCount++;
            }
        }

        /// <summary>中间件后端的本地替身：Bank / RTPC 的派发语义全在框架的 <see cref="MiddlewareAudioHandler"/> 里。</summary>
        private sealed class ContractHandler : MiddlewareAudioHandler
        {
            protected override IAudioMiddlewareBridge CreateDefaultBridge() => new ContractBridge();
        }

        #endregion 本地夹具 [LOCAL FIXTURES]

        #region Handler 外观派发 [HANDLER FACADE DISPATCH]

        [Test]
        public void MiddlewareHandler_LoadBank_WithoutCapability_ReturnsFalse()
        {
            var handler = new ContractHandler();
            handler.SetBridge(new BridgeWithoutCapabilities());
            Assert.IsFalse(handler.LoadBank("Master"));
            Assert.IsFalse(handler.UnloadBank("Master"));
            Assert.DoesNotThrow(() => handler.SetRtpc("Health", 1f, 0UL));
        }

        [Test]
        public void MiddlewareHandler_SetRtpc_Global_DispatchesToBridge()
        {
            var stub = new ContractBridge();
            var handler = new ContractHandler();
            handler.SetBridge(stub);

            handler.SetRtpc(null, 1f, 0UL);
            Assert.AreEqual(0, stub.RtpcCount);

            handler.SetRtpc("PlayerHealth", 0.2f, 0UL);
            Assert.AreEqual(0.2f, stub.RtpcValues["PlayerHealth"], 1e-5f);
            Assert.AreEqual(1, stub.RtpcCount);
        }

        [Test]
        public void MiddlewareHandler_SetRtpc_WithHandle_MapsToNativeInstanceId()
        {
            var stub = new ContractBridge();
            var handler = new ContractHandler();
            handler.SetBridge(stub);

            var request = new AudioPlayRequest(42, 1f, 1f, EAudioTrack.Sfx, 128, EAudioPlayFlags.Loop);
            ulong handle = handler.Play("event:/Hit", request, null);
            Assert.AreNotEqual(0UL, handle);

            handler.SetRtpc("Intensity", 0.9f, handle);
            Assert.IsTrue(stub.RtpcValues.ContainsKey("1:Intensity"),
                "句柄必须解析成桥的 instanceId 后再下发（Stub 实例键为 \"{instanceId}:{name}\"）");
            Assert.AreEqual(0.9f, stub.RtpcValues["1:Intensity"], 1e-5f);
        }

        [Test]
        public void MiddlewareHandler_SetRtpc_UnknownHandle_DoesNotTouchBridge()
        {
            var stub = new ContractBridge();
            var handler = new ContractHandler();
            handler.SetBridge(stub);

            handler.SetRtpc("Intensity", 0.5f, 999UL);
            Assert.AreEqual(0, stub.RtpcCount);
            Assert.AreEqual(0, stub.RtpcValues.Count);
        }

        [Test]
        public void AudioService_LoadBank_UnloadBank_SetRtpc_DispatchesToHandler()
        {
            // 走生成器发的内部接缝做无副作用换入换出：getter 会懒加载、setter 会 Internal_Init 且拒收 null
            AudioServiceHandler previous = AudioService.Internal_PeekHandler();
            var stub = new ContractBridge();
            var handler = new ContractHandler();
            handler.SetBridge(stub);
            AudioService.Internal_UseHandler(handler);
            try
            {
                Assert.IsTrue(AudioService.LoadBank("Init"));
                Assert.IsFalse(AudioService.LoadBank("Init"));
                Assert.IsTrue(AudioService.UnloadBank("Init"));
                Assert.IsFalse(AudioService.UnloadBank("Init"));

                AudioService.SetRtpc("MasterVolume", 0.4f);
                Assert.AreEqual(0.4f, stub.RtpcValues["MasterVolume"], 1e-5f);
            }
            finally
            {
                AudioService.Internal_UseHandler(previous);
            }
        }

        #endregion Handler 外观派发 [HANDLER FACADE DISPATCH]

        #region 加载失败可归因 [LOAD FAILURE DIAGNOSIS]

        [Test]
        public void MiddlewareHandler_LoadBank_Failed_WarnsOncePerPath()
        {
            AudioWarnOnce.Reset();
            var bridge = new ControllableBankBridge { NextResult = EAudioBankLoadResult.Failed };
            var handler = new ContractHandler();
            handler.SetBridge(bridge);

            UtfLogExpect.Warning();
            Assert.IsFalse(handler.LoadBank("Bank_Boss"), "失败必须返回 false");
            Assert.IsFalse(handler.LoadBank("Bank_Boss"));
            Assert.IsFalse(handler.LoadBank("Bank_Boss"));

            // 只声明了一条 Warning 预期：再多落一条告警就会以「意外日志」失败——这正是 WarnsOnce 的计数守卫
            Assert.AreEqual(3, bridge.LoadCalls, "告警去重不得顺手把重试也拦掉——桥仍要被问到");
        }

        [Test]
        public void MiddlewareHandler_LoadBank_AlreadyLoaded_StaysSilent()
        {
            AudioWarnOnce.Reset();
            var bridge = new ControllableBankBridge { NextResult = EAudioBankLoadResult.AlreadyLoaded };
            var handler = new ContractHandler();
            handler.SetBridge(bridge);

            // 幂等命中（含插件启动时自行加载的 master/Init 库）是正常路径：报出来就会把真失败淹成噪音
            Assert.IsFalse(handler.LoadBank("Master"));
            Assert.IsFalse(handler.LoadBank("Master"));
            Assert.AreEqual(2, bridge.LoadCalls);
        }

        [Test]
        public void MiddlewareHandler_LoadBank_EmptyPath_DoesNotTouchBridge()
        {
            AudioWarnOnce.Reset();
            var bridge = new ControllableBankBridge { NextResult = EAudioBankLoadResult.Failed };
            var handler = new ContractHandler();
            handler.SetBridge(bridge);

            Assert.IsFalse(handler.LoadBank(null));
            Assert.IsFalse(handler.LoadBank(string.Empty));
            Assert.AreEqual(0, bridge.LoadCalls, "空路径在外观层短路，不该占用失败告警的额度");
        }

        #endregion 加载失败可归因 [LOAD FAILURE DIAGNOSIS]
    }
}
