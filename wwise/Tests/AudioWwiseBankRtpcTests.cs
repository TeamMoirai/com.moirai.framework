using System;
using System.Reflection;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Audio.Middleware;
using Moirai.Atropos.Audio.Wwise;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// Wwise 插件后端的 Bank / RTPC 契约：Stub 的幂等语义、能力接口实现与默认桥工厂。
    /// </summary>
    /// <remarks>
    /// 随 <c>wwise</c> 插件走的本后端专属用例，从框架的 <c>AudioMiddlewareBankRtpcTests</c> 逐条搬来，断言与期望值未改。 <br />
    /// 不依赖 <c>WWISE_INSTALLED</c>：宏关时编的是 <see cref="WwiseBridgeStub"/>，宏开时 Native 桥经按名探测参与断言。 <br />
    /// 本装配看不到框架的 <c>internal</c> 面（<c>InternalsVisibleTo</c> 只授框架自家与三个测试装配，不逐插件放行）， <br />
    /// 故取默认桥走 <see cref="MiddlewareAudioHandler"/> 的 <c>CreateDefaultBridge</c> 窄反射——它是这条宏感知路径的唯一入口。
    /// </remarks>
    [TestFixture]
    public sealed class AudioWwiseBankRtpcTests
    {
        #region Stub 契约 [STUB CONTRACT]

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
        public void WwiseStub_ImplementsBankAndRtpcCapabilityInterfaces()
        {
            AssertBankAndRtpc(typeof(WwiseBridgeStub));
        }

        [Test]
        public void WwiseNativeBridge_WhenCompiled_ImplementsBankAndRtpcCapabilityInterfaces()
        {
            // 类型仅在 WWISE_INSTALLED 下存在；存在则必须补齐能力，缺宏时跳过
            Assembly plugin = typeof(WwiseBridgeStub).Assembly;

            Type wwiseNative = plugin.GetType("Moirai.Atropos.Audio.Wwise.WwiseBridgeNative");
            if (wwiseNative != null) AssertBankAndRtpc(wwiseNative);
        }

        [Test]
        public void WwiseDefaultBridgeFactory_ExposesBankAndRtpcCapabilities()
        {
            AssertBankAndRtpc(CreateDefaultBridge(typeof(WwiseAudioHandler)));
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
