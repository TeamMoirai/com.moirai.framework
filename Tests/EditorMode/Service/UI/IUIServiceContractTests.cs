using Moirai.Atropos;
using Moirai.Atropos.UI;
using UnityEngine;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// IUIService 契约绑定与显式实现转发的契约守卫。
    /// </summary>
    /// <remarks>
    /// UIService 双轨例外不声明 [ProviderHandlerHost]，契约绑定由组合根显式补登； <br />
    /// 本夹具用隔离世界复刻同一条绑定路径，验证解析与事件桥接机制。
    /// </remarks>
    [TestFixture]
    public class IUIServiceContractTests
    {
        private ServiceWorld _world;
        private UIService _ui;

        [SetUp]
        public void SetUp()
        {
            _world = new ServiceWorld();
            _ui = new UIService();
            // 组合根同款绑定路径：先注册实例，再显式补登 IUIService 契约绑定
            _world.Register(EServiceScopeKind.App, _ui);
            _world.Register(EServiceScopeKind.App, typeof(IUIService), _ui);
        }

        [TearDown]
        public void TearDown()
        {
            // 未激活（未 OnInit）的隔离世界不 Dispose：纯托管对象图交由 GC 回收
            _world = null;
            _ui = null;
        }

        [Test]
        public void ContractBinding_ResolvesFacadeInstance()
        {
            Assert.IsTrue(_world.TryGetService(out IUIService resolved),
                "IUIService 契约绑定未生效：组合根显式补登后 Context 解析应命中门面实例");
            Assert.AreSame(_ui, resolved, "契约解析应命中注册的同一 UIService 实例");
        }

        [Test]
        public void ContractQuery_MatchesStaticFacade()
        {
            Assert.IsTrue(_world.TryGetService(out IUIService resolved));

            // 轨无关查询面：契约实例与静态门面读同一条共享账本，空账本下两侧同为 null
            Assert.AreEqual(UIService.CurrentModal, resolved.CurrentModal);
            Assert.AreEqual(UIService.NavigationDepth, resolved.NavigationDepth);
        }

        [Test]
        public void ContractEventBridge_InstanceSubscriptionReceivesStaticRaise()
        {
            Assert.IsTrue(_world.TryGetService(out IUIService resolved));

            var window = new ProbeWindow();
            UIWindow received = null;

            resolved.onWindowShown += OnShown;
            try
            {
                UIService.Internal_RaiseWindowShown(window);
                Assert.AreSame(window, received, "经契约实例订阅应桥接到门面静态事件并收到广播");
            }
            finally
            {
                resolved.onWindowShown -= OnShown;
            }

            void OnShown(UIWindow shown) => received = shown;
        }

        /// <summary>事件桥接探针窗口（纯托管实例，不参与任何轨）。</summary>
        private sealed class ProbeWindow : UIWindow
        {
        }
    }
}
