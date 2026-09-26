using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using Moirai.Atropos.Audio.Middleware;
using Moirai.Atropos.Audio.Wwise;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Service.Audio
{
    /// <summary>
    /// 中间件 Bank / RTPC 能力契约：Stub 幂等语义、Handler 外观派发、真 SDK 桥的能力接口实现。
    /// <para>刻意不依赖 <c>FMOD_INSTALLED</c> / <c>WWISE_INSTALLED</c>：CI 无插件也能跑；
    /// Native 类型存在时（已定义宏）经反射断言其实现了能力接口。</para>
    /// </summary>
    [TestFixture]
    public sealed class AudioMiddlewareBankRtpcTests
    {
        #region Stub 契约 [STUB CONTRACT]

        [Test]
        public void FmodStub_LoadBank_EmptyOrNull_ReturnsFailed()
        {
            var stub = new FmodBridgeStub();
            Assert.AreEqual(EAudioBankLoadResult.Failed, stub.LoadBank(null));
            Assert.AreEqual(EAudioBankLoadResult.Failed, stub.LoadBank(string.Empty));
            Assert.AreEqual(0, stub.BankLoadCount);
            Assert.AreEqual(0, stub.Banks.Count);
        }

        [Test]
        public void FmodStub_LoadBank_Idempotent_SecondIsAlreadyLoaded()
        {
            var stub = new FmodBridgeStub();
            Assert.AreEqual(EAudioBankLoadResult.Loaded, stub.LoadBank("Master"));
            Assert.AreEqual(EAudioBankLoadResult.AlreadyLoaded, stub.LoadBank("Master"),
                "幂等命中必须与真失败分得开——它会被上层判成「正常」，不该进告警");
            Assert.AreEqual(1, stub.BankLoadCount);
            Assert.IsTrue(stub.Banks.Contains("Master"));
        }

        [Test]
        public void FmodStub_UnloadBank_NotLoadedOrEmpty_ReturnsFalse()
        {
            var stub = new FmodBridgeStub();
            Assert.IsFalse(stub.UnloadBank(null));
            Assert.IsFalse(stub.UnloadBank(string.Empty));
            Assert.IsFalse(stub.UnloadBank("Master"));
        }

        [Test]
        public void FmodStub_UnloadBank_Loaded_ReturnsTrueThenFalse()
        {
            var stub = new FmodBridgeStub();
            Assert.AreEqual(EAudioBankLoadResult.Loaded, stub.LoadBank("Master"));
            Assert.IsTrue(stub.UnloadBank("Master"));
            Assert.IsFalse(stub.UnloadBank("Master"), "卸载后再次 Unload 必须 false");
            Assert.IsFalse(stub.Banks.Contains("Master"));
        }

        [Test]
        public void FmodStub_SetRtpc_EmptyName_IsNoOp()
        {
            var stub = new FmodBridgeStub();
            stub.SetRtpc(null, 1f, 0UL);
            stub.SetRtpc(string.Empty, 1f, 0UL);
            Assert.AreEqual(0, stub.RtpcCount);
            Assert.AreEqual(0, stub.RtpcValues.Count);
        }

        [Test]
        public void FmodStub_SetRtpc_GlobalAndInstance_ScopedSeparately()
        {
            var stub = new FmodBridgeStub();
            stub.SetRtpc("Health", 0.25f, 0UL);
            stub.SetRtpc("Health", 0.75f, 7UL);

            Assert.AreEqual(0.25f, stub.RtpcValues["Health"], 1e-5f);
            Assert.AreEqual(0.75f, stub.RtpcValues["7:Health"], 1e-5f);
            Assert.AreEqual(2, stub.RtpcCount);
        }

        [Test]
        public void WwiseStub_LoadUnloadBank_ContractMatchesFmod()
        {
            var stub = new WwiseBridgeStub();
            Assert.AreEqual(EAudioBankLoadResult.Failed, stub.LoadBank(null));
            Assert.AreEqual(EAudioBankLoadResult.Failed, stub.LoadBank(string.Empty));
            Assert.AreEqual(EAudioBankLoadResult.Loaded, stub.LoadBank("Init"));
            Assert.AreEqual(EAudioBankLoadResult.AlreadyLoaded, stub.LoadBank("Init"), "已加载再 Load 必须算幂等命中");
            Assert.IsTrue(stub.UnloadBank("Init"));
            Assert.IsFalse(stub.UnloadBank("Init"));
            Assert.AreEqual(1, stub.BankLoadCount);
        }

        [Test]
        public void WwiseStub_SetRtpc_GlobalAndInstance_ScopedSeparately()
        {
            var stub = new WwiseBridgeStub();
            stub.SetRtpc(null, 1f, 0UL);
            Assert.AreEqual(0, stub.RtpcCount);

            stub.SetRtpc("PlayerHealth", 0.5f, 0UL);
            stub.SetRtpc("PlayerHealth", 0.1f, 3UL);
            Assert.AreEqual(0.5f, stub.RtpcValues["PlayerHealth"], 1e-5f);
            Assert.AreEqual(0.1f, stub.RtpcValues["3:PlayerHealth"], 1e-5f);
        }

        #endregion Stub 契约 [STUB CONTRACT]

        #region 能力接口实现 [CAPABILITY INTERFACES]

        [Test]
        public void Stubs_ImplementBankAndRtpcCapabilityInterfaces()
        {
            AssertBankAndRtpc(typeof(FmodBridgeStub));
            AssertBankAndRtpc(typeof(WwiseBridgeStub));
        }

        [Test]
        public void NativeBridges_WhenCompiled_ImplementBankAndRtpcCapabilityInterfaces()
        {
            // 类型仅在 FMOD_INSTALLED / WWISE_INSTALLED 下存在；存在则必须补齐能力，缺宏时跳过
            Assembly runtime = typeof(FmodBridgeStub).Assembly;

            Type fmodNative = runtime.GetType("Moirai.Atropos.Audio.Fmod.FmodBridgeNative");
            if (fmodNative != null) AssertBankAndRtpc(fmodNative);

            Type wwiseNative = runtime.GetType("Moirai.Atropos.Audio.Wwise.WwiseBridgeNative");
            if (wwiseNative != null) AssertBankAndRtpc(wwiseNative);
        }

        [Test]
        public void DefaultBridgeFactories_ExposeBankAndRtpcCapabilities()
        {
            AssertBankAndRtpc(CreateDefaultBridge(typeof(FmodAudioHandler)));
            AssertBankAndRtpc(CreateDefaultBridge(typeof(WwiseAudioHandler)));
        }

        /// <summary>断言桥类型（或实例）实现了 Bank / RTPC 能力接口。</summary>
        private static void AssertBankAndRtpc(object bridgeOrType)
        {
            Assert.IsNotNull(bridgeOrType);
            Type bridgeType = bridgeOrType as Type ?? bridgeOrType.GetType();
            Assert.IsTrue(typeof(IAudioMiddlewareBankControl).IsAssignableFrom(bridgeType),
                $"{bridgeType.Name} 必须实现 IAudioMiddlewareBankControl");
            Assert.IsTrue(typeof(IAudioMiddlewareRtpcControl).IsAssignableFrom(bridgeType),
                $"{bridgeType.Name} 必须实现 IAudioMiddlewareRtpcControl");
        }

        private static object CreateDefaultBridge(Type handlerType)
        {
            var handler = (MiddlewareAudioHandler)Activator.CreateInstance(handlerType);
            return handler.Internal_PeekDefaultBridge();
        }

        #endregion 能力接口实现 [CAPABILITY INTERFACES]

        #region Handler 外观派发 [HANDLER FACADE DISPATCH]

        [Test]
        public void MiddlewareHandler_LoadBank_UnloadBank_DispatchesToBridge()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
            handler.SetBridge(stub);

            Assert.IsFalse(handler.LoadBank(null));
            Assert.IsFalse(handler.LoadBank(string.Empty));
            Assert.AreEqual(0, stub.BankLoadCount);

            Assert.IsTrue(handler.LoadBank("Master"));
            Assert.IsFalse(handler.LoadBank("Master"), "Handler 层空路径短路后由桥做幂等");
            Assert.AreEqual(1, stub.BankLoadCount);

            Assert.IsTrue(handler.UnloadBank("Master"));
            Assert.IsFalse(handler.UnloadBank("Master"));
            Assert.AreEqual(0, stub.Banks.Count);
        }

        [Test]
        public void MiddlewareHandler_LoadBank_WithoutCapability_ReturnsFalse()
        {
            var handler = new FmodAudioHandler();
            handler.SetBridge(new BridgeWithoutCapabilities());
            Assert.IsFalse(handler.LoadBank("Master"));
            Assert.IsFalse(handler.UnloadBank("Master"));
            Assert.DoesNotThrow(() => handler.SetRtpc("Health", 1f, 0UL));
        }

        [Test]
        public void MiddlewareHandler_SetRtpc_Global_DispatchesToBridge()
        {
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
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
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
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
            var stub = new FmodBridgeStub();
            var handler = new FmodAudioHandler();
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
            var stub = new WwiseBridgeStub();
            var handler = new WwiseAudioHandler();
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

        /// <summary>仅实现主桥接口的假件——验证能力探测安全降级，也是能力子类化的底座。</summary>
        private class BridgeWithoutCapabilities : IAudioMiddlewareBridge
        {
            public bool Initialize(UnityEngine.Transform instanceRoot) => true;
            public void Shutdown() { }
            public void Update(float unscaledDeltaTime) { }
            public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, UnityEngine.Vector3? position3D) => 0UL;
            public void StopInstance(ulong instanceId, bool immediate) { }
            public void SetPaused(ulong instanceId, bool paused) { }
            public void SetInstanceVolume(ulong instanceId, float volume) { }
            public void SetBusVolume(string busPath, float volume) { }
            public float GetBusVolume(string busPath) => 0f;
            public bool IsPlaying(ulong instanceId) => false;
            public string GetEventPathFromClip(UnityEngine.AudioClip clip) => null;
        }

        #endregion Handler 外观派发 [HANDLER FACADE DISPATCH]

        #region 加载失败可归因 [LOAD FAILURE DIAGNOSIS]

        /// <summary>可编排加载结果、并统计触达次数的假桥。</summary>
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

        [Test]
        public void MiddlewareHandler_LoadBank_Failed_WarnsOncePerPath()
        {
            AudioWarnOnce.Reset();
            var bridge = new ControllableBankBridge { NextResult = EAudioBankLoadResult.Failed };
            var handler = new FmodAudioHandler();
            handler.SetBridge(bridge);

            LogAssert.Expect(UnityEngine.LogType.Warning, new Regex("声音库 .*Bank_Boss 加载失败"));
            Assert.IsFalse(handler.LoadBank("Bank_Boss"), "失败必须返回 false");
            Assert.IsFalse(handler.LoadBank("Bank_Boss"));
            Assert.IsFalse(handler.LoadBank("Bank_Boss"));

            // 三条断言只配了一次 LogAssert.Expect：再多落一条 Warning 就会以「意外日志」失败
            Assert.AreEqual(3, bridge.LoadCalls, "告警去重不得顺手把重试也拦掉——桥仍要被问到");
        }

        [Test]
        public void MiddlewareHandler_LoadBank_AlreadyLoaded_StaysSilent()
        {
            AudioWarnOnce.Reset();
            var bridge = new ControllableBankBridge { NextResult = EAudioBankLoadResult.AlreadyLoaded };
            var handler = new FmodAudioHandler();
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
            var handler = new FmodAudioHandler();
            handler.SetBridge(bridge);

            Assert.IsFalse(handler.LoadBank(null));
            Assert.IsFalse(handler.LoadBank(string.Empty));
            Assert.AreEqual(0, bridge.LoadCalls, "空路径在外观层短路，不该占用失败告警的额度");
        }

        #endregion 加载失败可归因 [LOAD FAILURE DIAGNOSIS]
    }
}
