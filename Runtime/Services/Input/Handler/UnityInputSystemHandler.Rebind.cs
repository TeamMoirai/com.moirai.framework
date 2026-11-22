#if ENABLE_INPUT_SYSTEM
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.InputSystem;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// <see cref="UnityInputSystemHandler"/> 的重绑定实现：交互式监听、冲突检测与绑定覆写读写。
    /// </summary>
    /// <remarks>
    /// 落绑走 <see cref="InputBinding.overridePath"/>：不篡改资产原始绑定，重置即清覆写； <br />
    /// 同一动作同时至多一个监听期，重复发起使上一个以 <see cref="ERebindOutcome.Cancelled"/> 终结； <br />
    /// 后端关停（<see cref="OnShutdown"/>）时未决监听一律取消。
    /// </remarks>
    partial class UnityInputSystemHandler : IInputRebindable
    {
        /// <inheritdoc />
        public UniTask<InputRebindResult> RebindAsync(string actionName, string actionGroup,
            InputRebindOptions options, CancellationToken cancelToken)
        {
            var action = ResolveRebindTarget(actionName, actionGroup);
            if (action == null ||
                ResolveOverrideTargetIndex(action, options.PartName) < 0)
                return UniTask.FromResult(InputRebindResult.Failed(ERebindOutcome.NotRebindable, actionGroup, actionName));

            // 前一个会话未决：先取消，保证同一动作同时至多一个监听期
            _activeSession?.Cancel(ERebindOutcome.Cancelled);

            var session = new RebindSession(this, action, actionGroup, actionName, options, cancelToken);
            _activeSession = session;
            return session.Run();
        }

        /// <inheritdoc />
        public bool TryGetBindingOverride(string actionName, string actionGroup, out InputBindingData data)
        {
            data = default;
            var action = GetInputAction(actionGroup, actionName);
            if (action == null) return false;

            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                // 逐绑定回读覆写：overridePath 非空即有效覆写；复合整体不参与，部件覆写带部件名返回
                string overridePath = binding.overridePath;
                if (string.IsNullOrEmpty(overridePath)) continue;
                if (binding.isComposite) continue;

                data = new InputBindingData(overridePath,
                    binding.isPartOfComposite ? binding.name : null, false);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public void ApplyBindingOverride(string actionName, string actionGroup, in InputBindingData data)
        {
            var action = ResolveRebindTarget(actionName, actionGroup);
            var fullActionName = new InputActionKey(actionGroup, actionName).ToFullName();
            if (action == null)
            {
                LogUtility.Warning($"ApplyBindingOverride: action '{fullActionName}' not found or not rebindable.");
                return;
            }

            if (string.IsNullOrEmpty(data.Path))
            {
                ResetBinding(actionName, actionGroup);
                return;
            }

            int bindingIndex = ResolveOverrideTargetIndex(action, data.PartName);
            if (bindingIndex < 0)
            {
                LogUtility.Warning($"ApplyBindingOverride: no override target on '{fullActionName}' (path '{data.Path}').");
                return;
            }

            action.ApplyBindingOverride(bindingIndex, data.Path);
            RefreshActionEnabled(action);

            string encoded = InputRebindSettingsPolicy.Encode(in data);
            if (encoded != null) GameSettings.SetString(InputRebindSettingsPolicy.BindingKey(fullActionName), encoded);
        }

        /// <inheritdoc />
        public void ResetBinding(string actionName, string actionGroup)
        {
            var action = GetInputAction(actionGroup, actionName);
            if (action == null) return;

            action.RemoveAllBindingOverrides();
            RefreshActionEnabled(action);
            InputRebindSettingsPolicy.RemoveBinding(new InputActionKey(actionGroup, actionName).ToFullName());
        }

        /// <inheritdoc />
        public void ResetAllBindings()
        {
            var asset = InputActions;
            if (asset == null) return;

            var maps = asset.actionMaps;
            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                map.RemoveAllBindingOverrides();

                // 同步清设置：逐动作删键（GameSettings 无前缀枚举能力，键由动作名拼装）
                var actions = map.actions;
                for (int j = 0; j < actions.Count; j++)
                {
                    InputRebindSettingsPolicy.RemoveBinding(
                        new InputActionKey(map.name, actions[j].name).ToFullName());
                }
            }
        }

        /// <summary>
        /// 解析重绑目标动作（不经查询门控——重绑发生在设置页，压制态不应把动作判成不存在）。
        /// </summary>
        private InputAction ResolveRebindTarget(string actionName, string actionGroup)
        {
            var asset = InputActions;
            if (asset == null) return null;

            string fullName = new InputActionKey(actionGroup, actionName).ToFullName();
            InputAction action = asset.FindAction(fullName);
            if (action == null || action.bindings.Count == 0) return null;

            return action;
        }

        /// <summary>
        /// 从 <see cref="GameSettings"/> 恢复已存的绑定覆写（OnInit 末尾调用，资产就位后执行）。
        /// </summary>
        /// <remarks>逐动作读键，无键跳过；已存值指向的动作/部件缺失（资产改版）时静默跳过保留默认绑定。</remarks>
        private void LoadPersistedOverrides()
        {
            var asset = InputActions;
            if (asset == null) return;

            var maps = asset.actionMaps;
            for (int i = 0; i < maps.Count; i++)
            {
                var map = maps[i];
                var actions = map.actions;
                for (int j = 0; j < actions.Count; j++)
                {
                    var action = actions[j];
                    string fullActionName = new InputActionKey(map.name, action.name).ToFullName();
                    string stored = GameSettings.GetString(InputRebindSettingsPolicy.BindingKey(fullActionName), null);
                    if (!InputRebindSettingsPolicy.TryDecode(stored, out var data)) continue;

                    int bindingIndex = ResolveOverrideTargetIndex(action, data.PartName);
                    if (bindingIndex < 0) continue;

                    action.ApplyBindingOverride(bindingIndex, data.Path);
                }
            }
        }

        /// <summary>
        /// 解析覆写应落到哪个绑定：带部件名时精确匹配同部件；否则取首个非复合非部件绑定。
        /// </summary>
        private static int ResolveOverrideTargetIndex(InputAction action, string partName)
        {
            var bindings = action.bindings;
            if (!string.IsNullOrEmpty(partName))
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i].isPartOfComposite &&
                        string.Equals(bindings[i].name, partName, StringComparison.Ordinal))
                        return i;
                }
                return -1;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                if (!bindings[i].isComposite && !bindings[i].isPartOfComposite) return i;
            }
            return -1;
        }

        /// <summary>
        /// 覆写变更后对齐动作启停（被压制 Map 的动作保持禁用，其余恢复启用）。
        /// </summary>
        private void RefreshActionEnabled(InputAction action)
        {
            bool suppressed = IsActionSuppressed(action);
            if (suppressed) action.Disable();
            else action.Enable();
        }

        /// <summary>
        /// 冲突检测：在资产全部动作中查找（目标动作之外）与候选路径生效指向同一控件的绑定。
        /// </summary>
        /// <remarks>按 <see cref="InputBinding.effectivePath"/> 判定——已被覆写遮蔽到同控件的绑定同样是冲突； <br />
        /// 复合壳（<see cref="InputBinding.isComposite"/>）是类型声明不是控件绑定，不参与；部件参与判定。</remarks>
        /// <returns>冲突绑定在其动作内的索引（经 <paramref name="conflictAction"/> 带出冲突动作）；无冲突为 -1。</returns>
        internal static int FindConflictBinding(InputActionAsset asset, InputAction target, string candidatePath,
            out InputAction conflictAction)
        {
            conflictAction = null;

            var maps = asset.actionMaps;
            for (int i = 0; i < maps.Count; i++)
            {
                var actions = maps[i].actions;
                for (int j = 0; j < actions.Count; j++)
                {
                    var other = actions[j];
                    if (ReferenceEquals(other, target)) continue;

                    var bindings = other.bindings;
                    for (int k = 0; k < bindings.Count; k++)
                    {
                        if (bindings[k].isComposite) continue;

                        if (string.Equals(bindings[k].effectivePath, candidatePath, StringComparison.Ordinal))
                        {
                            conflictAction = other;
                            return k;
                        }
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// 一次交互式重绑的监听会话：驱动 <see cref="InputActionRebindingExtensions.RebindingOperation"/>， <br />
        /// 候选路径经冲突判定后由会话显式落绑，发布 <see cref="InputRebindEvent"/> 并把结果回给等待方。
        /// </summary>
        private sealed class RebindSession
        {
            private readonly UnityInputSystemHandler _owner;
            private readonly InputAction _action;
            private readonly string _actionGroup;
            private readonly string _actionName;
            private readonly InputRebindOptions _options;
            private readonly CancellationTokenRegistration _cancelRegistration;

            private InputActionRebindingExtensions.RebindingOperation _operation;
            private UniTaskCompletionSource<InputRebindResult> _completion;
            private bool _finished;

            // OnApplyBinding 拦截到的候选控件路径（默认落绑被拦截，落绑由完成路径显式执行）
            private string _candidatePath;

            public RebindSession(UnityInputSystemHandler owner, InputAction action, string actionGroup,
                string actionName, InputRebindOptions options, CancellationToken cancelToken)
            {
                _owner = owner;
                _action = action;
                _actionGroup = actionGroup ?? string.Empty;
                _actionName = actionName ?? string.Empty;
                _options = options;

                if (cancelToken.CanBeCanceled)
                    _cancelRegistration = cancelToken.Register(static state => ((RebindSession)state).Cancel(ERebindOutcome.Cancelled), this);
            }

            /// <summary>
            /// 启动监听并返回等待方持有的任务。
            /// </summary>
            public UniTask<InputRebindResult> Run()
            {
                _completion = new UniTaskCompletionSource<InputRebindResult>();

                // 监听期把动作禁用：设置页重绑不应触发 gameplay 查询；监听结束按压制态恢复
                _action.Disable();

                _operation = _action.PerformInteractiveRebinding()
                    // 拦截默认落绑：候选路径先进冲突判定，再由本会话按策略落绑
                    .OnApplyBinding((operation, path) => _candidatePath = path)
                    .OnComplete(OnOperationComplete)
                    .OnCancel(_ =>
                    {
                        // 监听期取消（用户按取消控件）：终结为 Cancelled；operation 由 Finish 链路释放
                        Finish(InputRebindResult.Failed(ERebindOutcome.Cancelled, _actionGroup, _actionName));
                    });

                if (_options.ExcludeMouse)
                {
                    // 整设备前缀排除：鼠标与触屏不参与匹配（设置页由指针操作，点按钮不应落绑）
                    _operation.WithControlsExcluding("<Mouse>");
                    _operation.WithControlsExcluding("<Touchscreen>");
                }

                // 复合部件重绑：WithAction 会按动作期望（Vector2 等复合容器类型）过滤候选，
                // 把按键控件整体排除——部件期望显式改为按键（数字复合部件都是单控按压）
                if (!string.IsNullOrEmpty(_options.PartName))
                    _operation.WithExpectedControlType("Button");

                // 按钮动作不带默认取消热键（引擎只给非按钮动作加 Esc），这里统一补齐
                _operation.WithCancelingThrough("<Keyboard>/escape");
                _operation.Start();

                return _completion.Task;
            }

            /// <summary>
            /// 以指定终态结束会话（幂等：先到者胜）。
            /// </summary>
            public void Cancel(ERebindOutcome outcome)
            {
                if (_finished) return;
                _finished = true;

                _cancelRegistration.Dispose();
                _operation?.Cancel();
                _operation?.Dispose();
                _operation = null;
                RestoreActionState();
                Publish(outcome, null, null);
                _completion?.TrySetResult(InputRebindResult.Failed(outcome, _actionGroup, _actionName));
            }

            private void OnOperationComplete(InputActionRebindingExtensions.RebindingOperation operation)
            {
                if (_finished)
                {
                    operation.Dispose();
                    return;
                }
                _finished = true;

                _cancelRegistration.Dispose();
                operation.Dispose();
                _operation = null;

                string candidatePath = _candidatePath;
                if (string.IsNullOrEmpty(candidatePath))
                {
                    RestoreActionState();
                    Publish(ERebindOutcome.Cancelled, null, null);
                    _completion.TrySetResult(InputRebindResult.Failed(ERebindOutcome.Cancelled, _actionGroup, _actionName));
                    return;
                }

                int conflictIndex = FindConflictBinding(_owner.InputActions, _action, candidatePath,
                    out var conflictAction);
                if (conflictIndex >= 0 && _options.ConflictPolicy != ERebindConflictPolicy.Override)
                {
                    ERebindOutcome outcome = _options.ConflictPolicy == ERebindConflictPolicy.Cancel
                        ? ERebindOutcome.Cancelled
                        : ERebindOutcome.Conflict;
                    RestoreActionState();
                    Publish(outcome, null, GetFullName(conflictAction));
                    _completion.TrySetResult(InputRebindResult.Conflict(_actionGroup, _actionName, GetFullName(conflictAction)));
                    return;
                }

                if (conflictIndex >= 0)
                {
                    // Override 策略：抢走冲突控件——冲突绑定本就指回该控件则显式解绑，覆写遮蔽则清覆写回落
                    var binding = conflictAction.bindings[conflictIndex];
                    if (string.Equals(binding.path, candidatePath, StringComparison.Ordinal))
                        conflictAction.ApplyBindingOverride(conflictIndex, string.Empty);
                    else
                        conflictAction.RemoveBindingOverride(conflictIndex);
                }

                int targetIndex = ResolveOverrideTargetIndex(_action, _options.PartName);
                _action.ApplyBindingOverride(targetIndex, candidatePath);

                // 交互式落绑与显式 ApplyBindingOverride 同一口径：即落即持久化（复合部件带部件名编码）
                string fullActionName = new InputActionKey(_actionGroup, _actionName).ToFullName();
                string encoded = InputRebindSettingsPolicy.Encode(new InputBindingData(candidatePath, _options.PartName));
                if (encoded != null) GameSettings.SetString(InputRebindSettingsPolicy.BindingKey(fullActionName), encoded);

                RestoreActionState();
                string conflictedName = conflictIndex >= 0 ? GetFullName(conflictAction) : null;
                Publish(ERebindOutcome.Applied, candidatePath, conflictedName);
                _completion.TrySetResult(new InputRebindResult(ERebindOutcome.Applied, _actionGroup, _actionName, candidatePath, conflictedName));
            }

            private static string GetFullName(InputAction action)
            {
                return new InputActionKey(action.actionMap.name, action.name).ToFullName();
            }

            /// <summary>
            /// 监听结束后按压制态恢复动作启停（重绑前曾被禁用）。
            /// </summary>
            private void RestoreActionState()
            {
                if (_action.enabled) return;
                if (_owner.IsActionSuppressed(_action)) return;

                _action.Enable();
            }

            private void Publish(ERebindOutcome outcome, string bindingPath, string conflictedAction)
            {
                var result = conflictedAction != null && outcome == ERebindOutcome.Conflict
                    ? InputRebindResult.Conflict(_actionGroup, _actionName, conflictedAction)
                    : new InputRebindResult(outcome, _actionGroup, _actionName, bindingPath, conflictedAction);
                EventBus.Publish(new InputRebindEvent(in result));
            }

            private void Finish(InputRebindResult result)
            {
                if (_finished) return;
                _finished = true;

                _cancelRegistration.Dispose();
                // 由 OnCancel 回调链进入：operation 已在取消路径上，这里只做对称释放
                _operation?.Dispose();
                _operation = null;
                RestoreActionState();
                Publish(result.Outcome, result.BindingPath, result.ConflictedAction);
                _completion?.TrySetResult(result);
            }
        }
    }
}
#endif
