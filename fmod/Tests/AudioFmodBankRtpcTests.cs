using System;
using System.Reflection;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Fmod;
using Moirai.Atropos.Audio.Middleware;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// FMOD 插件后端的 Bank / RTPC 契约：Stub 的幂等语义、能力接口实现与默认桥工厂。
    /// </summary>
    /// <remarks>
    /// 随 <c>fmod</c> 插件走的本后端专属用例，从框架的 <c>AudioMiddlewareBankRtpcTests</c> 逐条搬来，断言与期望值未改。 <br />
    /// 不依赖 <c>FMOD_INSTALLED</c>：宏关时编的是 <see cref="FmodBridgeStub"/>，宏开时 Native 桥经按名探测参与断言。 <br />
    /// 本装配看不到框架的 <c>internal</c> 面（<c>InternalsVisibleTo</c> 只授框架自家与三个测试装配，不逐插件放行）， <br />
    /// 故取默认桥走 <see cref="CreateDefaultBridge"/> 的窄反射——它是这条宏感知路径的唯一入口，属行为契约守卫正当用途。
    /// </remarks>
    [TestFixture]
    public sealed class AudioFmodBankRtpcTests
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

        #endregion Stub 契约 [STUB CONTRACT]

        #region 能力接口实现 [CAPABILITY INTERFACES]

        [Test]
        public void FmodStub_ImplementsBankAndRtpcCapabilityInterfaces()
        {
            AssertBankAndRtpc(typeof(FmodBridgeStub));
        }

        [Test]
        public void FmodNativeBridge_WhenCompiled_ImplementsBankAndRtpcCapabilityInterfaces()
        {
            // 类型仅在 FMOD_INSTALLED 下存在；存在则必须补齐能力，缺宏时跳过
            Assembly plugin = typeof(FmodBridgeStub).Assembly;

            Type fmodNative = plugin.GetType("Moirai.Atropos.Audio.Fmod.FmodBridgeNative");
            if (fmodNative != null) AssertBankAndRtpc(fmodNative);
        }

        [Test]
        public void FmodDefaultBridgeFactory_ExposesBankAndRtpcCapabilities()
        {
            AssertBankAndRtpc(CreateDefaultBridge(typeof(FmodAudioHandler)));
        }

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

        /// <summary>
        /// 断言桥类型（或实例）实现了 Bank / RTPC 能力接口。
        /// </summary>
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
            MethodInfo factory = handlerType.BaseType
                .GetMethod("CreateDefaultBridge", BindingFlags.Instance | BindingFlags.NonPublic);
            return factory.Invoke(handler, null);
        }

        #endregion 能力接口实现 [CAPABILITY INTERFACES]
    }
}
