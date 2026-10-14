using System;
using Moirai.Atropos;
using NUnit.Framework;

namespace Core.FrameworkSettings
{
    /// <summary>
    /// 探针契约：仅继承 <see cref="FrameworkHandler"/>，不关联任何生产处理器契约。
    /// </summary>
    /// <remarks>不标 <c>[Serializable]</c> 且无生产 <c>[ProviderDropdown]</c> 字段引用本类型，不会进入资产下拉扫描。</remarks>
    internal abstract class ProbeHandler : FrameworkHandler
    {
    }

    /// <summary>
    /// 声明默认的探针实现。
    /// </summary>
    internal sealed class ProbeDeclaredDefaultHandler : ProbeHandler
    {
    }

    /// <summary>
    /// 探针宿主：声明默认 + settings 来源回空，复刻「settings 优先 / 声明默认兜底」生产形状。
    /// </summary>
    [ProviderHandlerHost(typeof(ProbeHandler), typeof(ProbeDeclaredDefaultHandler))]
    internal static partial class ProbeHost
    {
        /// <summary>settings 空档：模拟未配置，验证懒加载回退到声明默认。</summary>
        private static ProbeHandler GetHandlerFromSettings() => null;
    }

    /// <summary>
    /// ProviderHandlerHost 声明默认处理器的行为契约：生成器生成的 <c>CreateDefaultHandler</c> 实例化声明类型，且懒加载在 settings 空档时回退到它。
    /// </summary>
    /// <remarks>探针宿主住测试程序集，生成的工厂与接缝同程序集可见，无需 InternalsVisibleTo。</remarks>
    [TestFixture]
    public sealed class ProviderHandlerHostDefaultFactoryTests
    {
        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(ProbeHost.Internal_PeekHandler(),
                "探针宿主静态槽初始应为空（上个用例未还原）");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ProbeHost.Internal_PeekHandler()?.Internal_Shutdown();
            }
            finally
            {
                ProbeHost.Internal_UseHandler(null);
            }
        }

        /// <summary>
        /// 生成的兜底工厂必须实例化声明默认类型。
        /// </summary>
        [Test]
        public void CreateDefaultHandler_DeclaredDefault_InstantiatesDeclaredType()
        {
            ProbeHandler created = ProbeHost.CreateDefaultHandler();

            Assert.IsInstanceOf<ProbeDeclaredDefaultHandler>(created, "兜底工厂应实例化 [ProviderHandlerHost] 声明的默认处理器");
            Assert.IsFalse(created.IsInitialized, "工厂只创建不初始化，初始化由懒加载链路负责");
        }

        /// <summary>
        /// settings 来源返回 null 时懒加载回退到声明默认，且 CAS 就位后初始化一次。
        /// </summary>
        [Test]
        public void Handler_SettingsSourceNull_FallsBackToDeclaredDefault()
        {
            ProbeHandler resolved = ProbeHost.Handler;

            Assert.IsInstanceOf<ProbeDeclaredDefaultHandler>(resolved, "settings 空档应回退到声明默认");
            Assert.IsTrue(ProbeHost.IsValid);
            Assert.IsTrue(resolved.IsInitialized, "懒加载 CAS 就位后应完成 Internal_Init");
            Assert.AreSame(resolved, ProbeHost.Handler, "已就位处理器应稳定复用，不重复实例化");
        }
    }
}
