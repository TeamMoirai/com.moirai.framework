#if ENABLE_INPUT_SYSTEM
using Kernel.GameSettings;
using Moirai.Atropos;
using Moirai.Atropos.Input;
using NUnit.Framework;
using Settings = Moirai.Atropos.GameSettings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Service.Input
{
    /// <summary>
    /// 复合绑定（WASD 类）重绑的门禁：部件定位覆写、部件快照回读、持久化编码往返、 <br />
    /// 重启恢复与冲突检测对部件的参与、重置清尾。
    /// </summary>
    /// <remarks>
    /// 动作显式 <c>expectedControlLayout="Vector2"</c> 贴合真实 WASD 配置（交互监听经 PlayMode 设备流承担， <br />
    /// 此处只断覆写状态与冲突判定）；设置存储换 RecordingSettingHandler 记数替身，不碰 persistentDataPath。
    /// </remarks>
    [TestFixture]
    public sealed class UnityInputSystemHandlerRebindCompositeTests
    {
        private const string MOVE_KEY = "Setting.Input.Rebind.Player/Move";

        private InputActionAsset _asset;
        private UnityInputSystemHandler _handler;
        private GameSettingHandler _previousHandler;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var playerMap = new InputActionMap("Player");
            var move = playerMap.AddAction("Move", InputActionType.Value,
                expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            playerMap.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _asset.AddActionMap(playerMap);

            _handler = new UnityInputSystemHandler();
            _handler.m_InputActions = _asset;

            _previousHandler = Settings.Internal_PeekHandler();
            Settings.Internal_UseHandler(new RecordingSettingHandler());
            ((Moirai.Atropos.IServiceLifecycle)_handler).Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            ((Moirai.Atropos.IServiceLifecycle)_handler).Destroy();
            Object.DestroyImmediate(_asset);
            Settings.Internal_UseHandler(_previousHandler);
        }

        [Test]
        public void ApplyBindingOverride_ByPartName_OverridesOnlyThatPart()
        {
            _handler.ApplyBindingOverride("Move", "Player", new InputBindingData("<Keyboard>/i", "Up"));

            var bindings = _asset.FindAction("Player/Move").bindings;
            Assert.AreEqual("<Keyboard>/i", bindings[1].effectivePath, "Up 部件换为新控件");
            Assert.AreEqual("<Keyboard>/w", bindings[1].path, "部件原始绑定不被篡改");
            Assert.AreEqual("<Keyboard>/s", bindings[2].effectivePath, "Down 部件不受影响");
            Assert.AreEqual("<Keyboard>/a", bindings[3].effectivePath, "Left 部件不受影响");
            Assert.AreEqual("<Keyboard>/d", bindings[4].effectivePath, "Right 部件不受影响");
        }

        [Test]
        public void TryGetBindingOverride_CompositePart_ReturnsPartSnapshot()
        {
            _handler.ApplyBindingOverride("Move", "Player", new InputBindingData("<Keyboard>/i", "Up"));

            Assert.IsTrue(_handler.TryGetBindingOverride("Move", "Player", out var data));
            Assert.AreEqual("<Keyboard>/i", data.Path);
            Assert.AreEqual("Up", data.PartName, "部件覆写带部件名回读");
        }

        [Test]
        public void ApplyBindingOverride_CompositePart_PersistsEncodedPartName()
        {
            _handler.ApplyBindingOverride("Move", "Player", new InputBindingData("<Keyboard>/i", "Up"));

            Assert.IsTrue(Settings.HasSetting(MOVE_KEY), "部件覆写即落键");
            Assert.AreEqual("<Keyboard>/i|Up", Settings.GetString(MOVE_KEY), "编码保留部件定位");
        }

        [Test]
        public void Restart_RestoresCompositePartOverride_FromPersistedKey()
        {
            _handler.ApplyBindingOverride("Move", "Player", new InputBindingData("<Keyboard>/i", "Up"));
            ((Moirai.Atropos.IServiceLifecycle)_handler).Destroy();

            var restored = new UnityInputSystemHandler();
            restored.m_InputActions = _asset;
            ((Moirai.Atropos.IServiceLifecycle)restored).Initialize();

            var bindings = _asset.FindAction("Player/Move").bindings;
            Assert.AreEqual("<Keyboard>/i", bindings[1].effectivePath, "重启后按部件恢复覆写");
            Assert.AreEqual("<Keyboard>/s", bindings[2].effectivePath, "其余部件保持默认");
            ((Moirai.Atropos.IServiceLifecycle)restored).Destroy();
        }

        [Test]
        public void FindConflictBinding_CandidateHitsPart_LocatesCompositeAction()
        {
            // Jump 重绑到 w：占用方是 Move 的 Up 部件（部件按生效路径参与冲突判定）
            var jump = _asset.FindAction("Player/Jump");
            int conflicted = UnityInputSystemHandler.FindConflictBinding(_asset, jump, "<Keyboard>/w",
                out var conflictAction);

            Assert.AreEqual(1, conflicted, "冲突定位到 Up 部件绑定索引");
            Assert.AreEqual("Move", conflictAction.name);
        }

        [Test]
        public void ResetBinding_ClearsAllCompositeParts()
        {
            _handler.ApplyBindingOverride("Move", "Player", new InputBindingData("<Keyboard>/i", "Up"));
            _handler.ApplyBindingOverride("Move", "Player", new InputBindingData("<Keyboard>/k", "Down"));

            _handler.ResetBinding("Move", "Player");

            var bindings = _asset.FindAction("Player/Move").bindings;
            Assert.AreEqual("<Keyboard>/w", bindings[1].effectivePath, "Up 回落默认");
            Assert.AreEqual("<Keyboard>/s", bindings[2].effectivePath, "Down 回落默认");
            Assert.IsFalse(Settings.HasSetting(MOVE_KEY), "持久化键同步清除");
        }
    }
}
#endif
