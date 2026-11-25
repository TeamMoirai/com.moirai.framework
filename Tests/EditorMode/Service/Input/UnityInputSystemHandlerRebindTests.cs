#if ENABLE_INPUT_SYSTEM
using Kernel.GameSettings;
using Moirai.Atropos;
using Moirai.Atropos.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Service.Input
{
    /// <summary>
    /// 重绑定覆写读写与冲突检测的门禁：代码构造 InputActionAsset，断言覆写落点、回读快照与冲突定位。
    /// </summary>
    /// <remarks>
    /// 只断覆写状态与冲突判定，不模拟设备输入（交互式监听经 RebindingOperation 依赖真实设备事件流， <br />
    /// 由 PlayMode/Player 侧承担）；设置存储换 RecordingSettingHandler 记数替身，用例不碰 persistentDataPath。
    /// </remarks>
    [TestFixture]
    public sealed class UnityInputSystemHandlerRebindTests
    {
        private InputActionAsset _asset;
        private InputActionMap _playerMap;
        private InputActionMap _uiMap;
        private UnityInputSystemHandler _handler;
        private GameSettingHandler _previousHandler;

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

            _previousHandler = GameSettings.Internal_PeekHandler();
            GameSettings.Internal_UseHandler(new RecordingSettingHandler());
            ((IServiceLifecycle)_handler).Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            ((IServiceLifecycle)_handler).Destroy();
            Object.DestroyImmediate(_asset);
            GameSettings.Internal_UseHandler(_previousHandler);
        }

        [Test]
        public void ApplyBindingOverride_Path_OverridesEffectiveBinding()
        {
            _handler.ApplyBindingOverride("Jump", "Player", new InputBindingData("<Keyboard>/e"));

            var action = _asset.FindAction("Player/Jump");
            Assert.AreEqual("<Keyboard>/e", action.bindings[0].effectivePath, "生效路径换为新控件");
            Assert.AreEqual("<Keyboard>/space", action.bindings[0].path, "资产原始绑定不被篡改");
        }

        [Test]
        public void TryGetBindingOverride_AfterApply_ReturnsSnapshot()
        {
            _handler.ApplyBindingOverride("Jump", "Player", new InputBindingData("<Keyboard>/e"));

            Assert.IsTrue(_handler.TryGetBindingOverride("Jump", "Player", out var data));
            Assert.AreEqual("<Keyboard>/e", data.Path);
            Assert.IsNull(data.PartName);
        }

        [Test]
        public void TryGetBindingOverride_WithoutOverride_ReturnsFalse()
        {
            Assert.IsFalse(_handler.TryGetBindingOverride("Jump", "Player", out _), "无覆写时无快照");
        }

        [Test]
        public void ResetBinding_RemovesOverride_AndClearsPersistedKey()
        {
            _handler.ApplyBindingOverride("Jump", "Player", new InputBindingData("<Keyboard>/e"));

            _handler.ResetBinding("Jump", "Player");

            var action = _asset.FindAction("Player/Jump");
            Assert.AreEqual("<Keyboard>/space", action.bindings[0].effectivePath, "重置回到资产默认");
            Assert.IsFalse(_handler.TryGetBindingOverride("Jump", "Player", out _));
            Assert.IsFalse(GameSettings.HasSetting(InputRebindSettingsPolicy.BindingKey("Player/Jump")), "持久化键同步清除");
        }

        [Test]
        public void ApplyBindingOverride_EmptyPath_RestoresDefault()
        {
            _handler.ApplyBindingOverride("Jump", "Player", new InputBindingData("<Keyboard>/e"));

            _handler.ApplyBindingOverride("Jump", "Player", default);

            var action = _asset.FindAction("Player/Jump");
            Assert.AreEqual("<Keyboard>/space", action.bindings[0].effectivePath, "空路径即移除覆写");
        }

        [Test]
        public void ApplyBindingOverride_UnknownAction_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                _handler.ApplyBindingOverride("Missing", "Player", new InputBindingData("<Keyboard>/e")));
            Assert.IsFalse(_handler.TryGetBindingOverride("Missing", "Player", out _));
        }

        [Test]
        public void FindConflictBinding_SameControlOnOtherAction_LocatesConflictedAction()
        {
            // Jump 占 <Keyboard>/space；对 Submit 发起同路径重绑前，先经冲突检测断言定位与排除自身
            var submit = _asset.FindAction("UI/Submit");
            int conflicted = UnityInputSystemHandler.FindConflictBinding(_asset, submit, "<Keyboard>/space",
                out var conflictAction);
            Assert.AreEqual(0, conflicted, "冲突定位到占用方绑定的索引");
            Assert.IsNotNull(conflictAction);
            Assert.AreEqual("Jump", conflictAction.name);

            var jump = _asset.FindAction("Player/Jump");
            Assert.AreEqual(-1, UnityInputSystemHandler.FindConflictBinding(_asset, jump, "<Keyboard>/space", out _),
                "自身占用不构成冲突");
            Assert.AreEqual(-1, UnityInputSystemHandler.FindConflictBinding(_asset, jump, "<Keyboard>/q", out _),
                "无占用不构成冲突");
        }

        [Test]
        public void FindConflictBinding_OverriddenBinding_MatchesByEffectivePath()
        {
            // Submit 已覆写到 <Keyboard>/q：Jump 重绑到 q 应按生效路径判冲突
            _handler.ApplyBindingOverride("Submit", "UI", new InputBindingData("<Keyboard>/q"));
            var jump = _asset.FindAction("Player/Jump");

            int conflicted = UnityInputSystemHandler.FindConflictBinding(_asset, jump, "<Keyboard>/q",
                out var conflictAction);
            Assert.AreEqual(0, conflicted, "覆写遮蔽到同控件的绑定同样构成冲突");
            Assert.AreEqual("Submit", conflictAction.name);
        }

        [Test]
        public void RebindAsync_UnknownAction_CompletesNotRebindable()
        {
            var result = _handler.RebindAsync("Missing", "Player", InputRebindOptions.Default,
                System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(ERebindOutcome.NotRebindable, result.Outcome);
        }

        [Test]
        public void RebindAsync_ConcurrentCallOnSameAction_SecondCancelsFirst()
        {
            var jump = _asset.FindAction("Player/Jump");

            var first = _handler.RebindAsync("Jump", "Player", InputRebindOptions.Default,
                System.Threading.CancellationToken.None);
            Assert.IsFalse(jump.enabled, "监听期动作禁用");

            var second = _handler.RebindAsync("Jump", "Player", InputRebindOptions.Default,
                System.Threading.CancellationToken.None);

            var firstResult = first.GetAwaiter().GetResult();
            Assert.AreEqual(ERebindOutcome.Cancelled, firstResult.Outcome, "同动作重复发起使前一个取消");
        }

        [Test]
        public void RebindAsync_AfterStart_ActionDisabled()
        {
            var task = _handler.RebindAsync("Jump", "Player", InputRebindOptions.Default,
                System.Threading.CancellationToken.None);

            var jump = _asset.FindAction("Player/Jump");
            Assert.IsFalse(jump.enabled, "监听期动作禁用，避免重绑触发 gameplay 查询");

            ((IServiceLifecycle)_handler).Destroy();
            var shutdownResult = task.GetAwaiter().GetResult();
            Assert.AreEqual(ERebindOutcome.Cancelled, shutdownResult.Outcome, "后端关停取消未决监听");
        }
    }
}
#endif
