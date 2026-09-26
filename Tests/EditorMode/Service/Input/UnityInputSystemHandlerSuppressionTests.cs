#if ENABLE_INPUT_SYSTEM
using Moirai.Atropos.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Service.Input
{
    /// <summary>
    /// <see cref="UnityInputSystemHandler"/> 压制门控测试：代码内构造 InputActionAsset（Player/UI 双 Map），
    /// 验证上下文 Map 随压制态整体启停、自定义资产启用所有权与查询降级语义。
    /// <para>纯状态断言（Map/Asset enabled），不模拟设备输入。</para>
    /// </summary>
    [TestFixture]
    public sealed class UnityInputSystemHandlerSuppressionTests
    {
        private InputActionAsset _asset;
        private InputActionMap _playerMap;
        private InputActionMap _uiMap;
        private UnityInputSystemHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _playerMap = new InputActionMap("Player");
            _playerMap.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _uiMap = new InputActionMap("UI");
            _uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            _asset.AddActionMap(_playerMap);
            _asset.AddActionMap(_uiMap);

            _handler = new UnityInputSystemHandler();
            _handler.m_InputActions = _asset;

            _handler.Internal_Init();
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Internal_Shutdown();
            Object.DestroyImmediate(_asset);
        }

        [Test]
        public void Init_EnablesCustomAsset_AndAllMapsEnabled()
        {
            Assert.IsTrue(_asset.enabled, "自定义资产应由处理器接管启用");
            Assert.IsTrue(_playerMap.enabled);
            Assert.IsTrue(_uiMap.enabled);
        }

        [Test]
        public void Disabled_SuppressesAllContextMaps()
        {
            _handler.Enabled = false;

            Assert.IsFalse(_playerMap.enabled);
            Assert.IsFalse(_uiMap.enabled);
            Assert.IsFalse(_handler.GetBool("Jump", "Player"), "全局硬门控：动作查询应降级");

            _handler.Enabled = true;

            Assert.IsTrue(_playerMap.enabled);
            Assert.IsTrue(_uiMap.enabled);
        }

        [Test]
        public void Lock_SuppressesPlayerMapOnly()
        {
            _handler.LockPlayerController = true;

            Assert.IsFalse(_playerMap.enabled, "玩家压制应禁用玩家 Map");
            Assert.IsTrue(_uiMap.enabled, "UI Map 不应受玩家压制影响");

            _handler.LockPlayerController = false;

            Assert.IsTrue(_playerMap.enabled);
        }

        [Test]
        public void UIModal_SuppressesPlayerMapOnly()
        {
            _handler.SetUIModal(true);

            Assert.IsFalse(_playerMap.enabled, "模态打开应断开玩家 Map");
            Assert.IsTrue(_uiMap.enabled, "模态自身热键依赖 UI Map 保持可用");

            _handler.SetUIModal(false);

            Assert.IsTrue(_playerMap.enabled);
        }

        [Test]
        public void PreventUI_SuppressesUIMapOnly()
        {
            _handler.PreventInteractionUI = true;

            Assert.IsFalse(_uiMap.enabled, "UI 压制应禁用 UI Map");
            Assert.IsTrue(_playerMap.enabled, "玩家 Map 不应受 UI 压制影响");

            _handler.PreventInteractionUI = false;

            Assert.IsTrue(_uiMap.enabled);
        }

        [Test]
        public void OverlappingSuppression_PlayerMapStaysDisabledUntilAllCleared()
        {
            _handler.LockPlayerController = true;
            _handler.SetUIModal(true);

            _handler.LockPlayerController = false;
            Assert.IsFalse(_playerMap.enabled, "模态仍在，玩家 Map 应保持禁用");

            _handler.SetUIModal(false);
            Assert.IsTrue(_playerMap.enabled, "压制全部解除后玩家 Map 应恢复");
        }

        [Test]
        public void Shutdown_DisablesOwnedAsset()
        {
            _handler.Internal_Shutdown();

            Assert.IsFalse(_asset.enabled, "由处理器启用的自定义资产应在关闭时对称禁用");

            // TearDown 幂等：重复 Shutdown 不应再触碰资产
            _handler.Internal_Shutdown();
        }
    }
}
#endif
