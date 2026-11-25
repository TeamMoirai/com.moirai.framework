#if ENABLE_INPUT_SYSTEM
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace Service.Input
{
    /// <summary>
    /// 交互式重绑的真实设备流门禁：InputTestFixture 注入键盘并驱动按键事件， <br />
    /// 断言 Applied / Cancelled / Conflict / Override 四终态的落绑、持久化与冲突处置。
    /// </summary>
    /// <remarks>
    /// 覆写状态与结果快照按终态断言；设置存储换内存替身（RecordingSettingHandler 只编进 EditorMode 装配）。 <br />
    /// 等待窗口 0.5s 覆盖 OnMatchWaitForAnother（0.05s）的完成延迟，UniTask 以 Status 判完成避免悬挂。
    /// </remarks>
    [TestFixture]
    public sealed class InputRebindPlayModeTests : InputTestFixture
    {
        private InputActionAsset _asset;
        private UnityInputSystemHandler _handler;
        private GameSettingHandler _previousHandler;
        private InMemorySettingHandler _settings;
        private Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var playerMap = new InputActionMap("Player");
            playerMap.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            var move = playerMap.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            var uiMap = new InputActionMap("UI");
            uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            _asset.AddActionMap(playerMap);
            _asset.AddActionMap(uiMap);

            _handler = new UnityInputSystemHandler();
            _handler.m_InputActions = _asset;

            _previousHandler = GameSettings.Internal_PeekHandler();
            _settings = new InMemorySettingHandler();
            GameSettings.Internal_UseHandler(_settings);

            _keyboard = InputSystem.AddDevice<Keyboard>();
            ((IServiceLifecycle)_handler).Initialize();
        }

        public override void TearDown()
        {
            ((IServiceLifecycle)_handler).Destroy();
            Object.DestroyImmediate(_asset);
            GameSettings.Internal_UseHandler(_previousHandler);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator RebindAsync_PressNewKey_CompletesApplied()
        {
            var jump = _asset.FindAction("Player/Jump");
            UniTask<InputRebindResult> task = _handler.RebindAsync("Jump", "Player",
                InputRebindOptions.Default, System.Threading.CancellationToken.None);
            Assert.IsFalse(jump.enabled, "监听期动作禁用");

            Press(_keyboard.eKey);
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "按键后监听应完成");
            InputRebindResult result = task.GetAwaiter().GetResult();
            Assert.AreEqual(ERebindOutcome.Applied, result.Outcome);
            Assert.AreEqual("<Keyboard>/e", result.BindingPath);
            Assert.AreEqual("<Keyboard>/e", jump.bindings[0].effectivePath, "生效路径换为新控件");
            Assert.AreEqual("<Keyboard>/space", jump.bindings[0].path, "资产原始绑定不被篡改");
            Assert.IsTrue(jump.enabled, "监听结束按压制态恢复启用");

            Assert.IsTrue(_settings.TryGetStored("Setting.Input.Rebind.Player/Jump", out string stored),
                "落绑即持久化");
            Assert.AreEqual("<Keyboard>/e", stored);
        }

        [UnityTest]
        public IEnumerator RebindAsync_PressCancelKey_CompletesCancelled()
        {
            var task = _handler.RebindAsync("Jump", "Player",
                InputRebindOptions.Default, System.Threading.CancellationToken.None);

            Press(_keyboard.escapeKey);
            yield return new WaitForSeconds(0.5f);

            InputRebindResult result = task.GetAwaiter().GetResult();
            Assert.AreEqual(ERebindOutcome.Cancelled, result.Outcome, "取消控件终结监听");
            var jump = _asset.FindAction("Player/Jump");
            Assert.AreEqual("<Keyboard>/space", jump.bindings[0].effectivePath, "取消不落绑");
            Assert.IsFalse(_settings.HasSetting("Setting.Input.Rebind.Player/Jump"), "取消不写设置键");
        }

        [UnityTest]
        public IEnumerator RebindAsync_ConflictingControl_FailPolicy_KeepsOriginalBinding()
        {
            var submit = _asset.FindAction("UI/Submit");
            UniTask<InputRebindResult> task = _handler.RebindAsync("Submit", "UI",
                InputRebindOptions.Default, System.Threading.CancellationToken.None);

            Press(_keyboard.spaceKey);
            yield return new WaitForSeconds(0.5f);

            InputRebindResult result = task.GetAwaiter().GetResult();
            Assert.AreEqual(ERebindOutcome.Conflict, result.Outcome);
            Assert.AreEqual("Player/Jump", result.ConflictedAction, "冲突定位到占用方动作");
            Assert.AreEqual("<Keyboard>/enter", submit.bindings[0].effectivePath, "冲突不落绑");
            Assert.IsFalse(_settings.HasSetting("Setting.Input.Rebind.UI/Submit"), "冲突不写设置键");
        }

        [UnityTest]
        public IEnumerator RebindAsync_ConflictingControl_OverridePolicy_StealsControl()
        {
            var submit = _asset.FindAction("UI/Submit");
            var jump = _asset.FindAction("Player/Jump");
            var options = new InputRebindOptions
            {
                ConflictPolicy = ERebindConflictPolicy.Override,
                ExcludeMouse = true,
            };
            UniTask<InputRebindResult> task = _handler.RebindAsync("Submit", "UI",
                options, System.Threading.CancellationToken.None);

            Press(_keyboard.spaceKey);
            yield return new WaitForSeconds(0.5f);

            InputRebindResult result = task.GetAwaiter().GetResult();
            Assert.AreEqual(ERebindOutcome.Applied, result.Outcome);
            Assert.AreEqual("<Keyboard>/space", submit.bindings[0].effectivePath, "新绑定落到目标动作");
            Assert.AreEqual(string.Empty, jump.bindings[0].effectivePath, "原占用绑定被显式解绑（指回原控件则解绑而非回落）");
        }

        [UnityTest]
        public IEnumerator RebindAsync_CancellationRequested_CompletesCancelled()
        {
            using (var cts = new System.Threading.CancellationTokenSource())
            {
                var task = _handler.RebindAsync("Jump", "Player",
                    InputRebindOptions.Default, cts.Token);

                cts.Cancel();
                yield return null;

                InputRebindResult result = task.GetAwaiter().GetResult();
                Assert.AreEqual(ERebindOutcome.Cancelled, result.Outcome, "取消令牌终结监听");
                Assert.AreEqual("<Keyboard>/space",
                    _asset.FindAction("Player/Jump").bindings[0].effectivePath, "令牌取消不落绑");
            }
        }

        [UnityTest]
        public IEnumerator RebindAsync_CompositeParts_SequentialRebinds_ApplyInOrder()
        {
            // WASD 复合的顺序重绑：up→i、left→j、down→k、right→l，逐段断言落绑与部件化持久化
            var move = _asset.FindAction("Player/Move");
            Assert.AreEqual("<Keyboard>/w", move.bindings[1].effectivePath, "前置：Up 部件为默认 W");

            yield return RebindPartAndPress("Up", _keyboard.iKey);
            Assert.AreEqual(ERebindOutcome.Applied, _lastResult.Outcome, "Up 部件重绑完成");
            Assert.AreEqual("<Keyboard>/i", move.bindings[1].effectivePath);

            yield return RebindPartAndPress("Left", _keyboard.jKey);
            Assert.AreEqual(ERebindOutcome.Applied, _lastResult.Outcome, "Left 部件重绑完成");
            Assert.AreEqual("<Keyboard>/j", move.bindings[3].effectivePath);

            yield return RebindPartAndPress("Down", _keyboard.kKey);
            Assert.AreEqual(ERebindOutcome.Applied, _lastResult.Outcome, "Down 部件重绑完成");
            Assert.AreEqual("<Keyboard>/k", move.bindings[2].effectivePath);

            yield return RebindPartAndPress("Right", _keyboard.lKey);
            Assert.AreEqual(ERebindOutcome.Applied, _lastResult.Outcome, "Right 部件重绑完成");
            Assert.AreEqual("<Keyboard>/l", move.bindings[4].effectivePath);

            // 部件原始绑定不被篡改；持久化编码保留部件定位（后写覆盖同键）
            Assert.AreEqual("<Keyboard>/w", move.bindings[1].path, "部件原始绑定不被篡改");
            Assert.IsTrue(_settings.TryGetStored("Setting.Input.Rebind.Player/Move", out string storedUp));
            Assert.AreEqual("<Keyboard>/l|Right", storedUp);
        }

        // 协程无法携带 out 参数：最后一段重绑的终态经字段带出
        private InputRebindResult _lastResult;

        /// <summary>
        /// 单部件交互重绑：发起监听 → 按指定键 → 等待窗口内断言完成并把终态写入 <see cref="_lastResult"/>。
        /// </summary>
        private IEnumerator RebindPartAndPress(string partName, ButtonControl control)
        {
            UniTask<InputRebindResult> task = _handler.RebindAsync("Move", "Player",
                new InputRebindOptions { ConflictPolicy = ERebindConflictPolicy.Fail, ExcludeMouse = true, PartName = partName },
                System.Threading.CancellationToken.None);

            Press(control);
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, $"{partName} 部件监听应在等待窗口内完成");
            _lastResult = task.GetAwaiter().GetResult();
        }

        /// <summary>内存设置替身：只记键值不落盘，供 PlayMode 装配验证持久化写入路径。</summary>
        private sealed class InMemorySettingHandler : GameSettingHandler
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

            internal bool TryGetStored(string key, out string value) => _values.TryGetValue(key, out value);

            public override int Count => _values.Count;
            public override bool Load() => true;
            public override bool Save() => true;
            public override string[] GetAllSettingNames() => new List<string>(_values.Keys).ToArray();
            public override void GetAllSettingNames(List<string> results)
            {
                results.Clear();
                results.AddRange(_values.Keys);
            }
            public override bool HasSetting(string settingName) => _values.ContainsKey(settingName);
            public override bool RemoveSetting(string settingName) => _values.Remove(settingName);
            public override void RemoveAllSettings() => _values.Clear();
            public override bool GetBool(string settingName) => int.Parse(Fetch(settingName)) != 0;
            public override bool GetBool(string settingName, bool defaultValue) =>
                _values.TryGetValue(settingName, out string value) ? int.Parse(value) != 0 : defaultValue;
            public override void SetBool(string settingName, bool value) => _values[settingName] = value ? "1" : "0";
            public override int GetInt(string settingName) => int.Parse(Fetch(settingName));
            public override int GetInt(string settingName, int defaultValue) =>
                _values.TryGetValue(settingName, out string value) ? int.Parse(value) : defaultValue;
            public override void SetInt(string settingName, int value) => _values[settingName] = value.ToString();
            public override float GetFloat(string settingName) => float.Parse(Fetch(settingName));
            public override float GetFloat(string settingName, float defaultValue) =>
                _values.TryGetValue(settingName, out string value) ? float.Parse(value) : defaultValue;
            public override void SetFloat(string settingName, float value) => _values[settingName] = value.ToString();
            public override string GetString(string settingName) => Fetch(settingName);
            public override string GetString(string settingName, string defaultValue) =>
                _values.TryGetValue(settingName, out string value) ? value : defaultValue;
            public override void SetString(string settingName, string value) => _values[settingName] = value;

            private string Fetch(string settingName) => _values[settingName];
        }
    }
}
#endif
