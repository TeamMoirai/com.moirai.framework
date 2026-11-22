using Moirai.Atropos;
using Moirai.Atropos.UI;
using UnityEngine;
using NUnit.Framework;

namespace Service.UI
{
    /// <summary>
    /// IUIService 契约 DIM 落地与门面静态语义对齐的契约守卫。
    /// </summary>
    /// <remarks>
    /// 回摆口径：IUIService 不再有单一注册实体——两支后端 handler 直接落地契约（DIM 全量转发静态门面）； <br />
    /// 本夹具用后端 handler 验证契约实例与静态门面读同一语义源、事件直订门面静态广播。
    /// </remarks>
    [TestFixture]
    public class IUIServiceContractTests
    {
        private ServiceWorld _world;
        private UITKHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _world = new ServiceWorld();
            _handler = new UITKHandler();
            // 回摆后契约键 = 各自具体类型（RegisterEnabledHandlers 同款路径）：handler 即注册实体
            _world.Register(EServiceScopeKind.App, _handler);
        }

        [TearDown]
        public void TearDown()
        {
            // 未激活（未 OnInit）的隔离世界不 Dispose：纯托管对象图交由 GC 回收
            _world = null;
            _handler = null;
        }

        [Test]
        public void ContractInstance_QueryMatchesStaticFacade()
        {
            Assert.IsTrue(_world.TryGetService(out UITKHandler resolved));

            // 轨无关查询面：DIM 默认实现与静态门面读同一条共享账本，空账本下两侧同为 null
            Assert.AreEqual(UIService.CurrentModal, ((IUIService)resolved).CurrentModal);
            Assert.AreEqual(UIService.NavigationDepth, ((IUIService)resolved).NavigationDepth);
        }

        [Test]
        public void ContractInstance_StaticEventSubscriptionReceivesRaise()
        {
            Assert.IsTrue(_world.TryGetService(out UITKHandler resolved));

            var window = new ProbeWindow();
            UIWindow received = null;

            // 回摆后契约事件成员已移除：消费方直订门面静态事件（同一语义源）
            UIService.onWindowShown += OnShown;
            try
            {
                UIService.Internal_RaiseWindowShown(window);
                Assert.AreSame(window, received, "直订门面静态事件应收到广播");
            }
            finally
            {
                UIService.onWindowShown -= OnShown;
            }

            void OnShown(UIWindow shown) => received = shown;
        }

        /// <summary>事件桥接探针窗口（纯托管实例，不参与任何轨）。</summary>
        private sealed class ProbeWindow : UIWindow
        {
        }
    }
}
