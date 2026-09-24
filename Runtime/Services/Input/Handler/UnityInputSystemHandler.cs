#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 基于 Unity Input System（Package），需定义 ENABLE_INPUT_SYSTEM。
    /// <para>压制门控（中心化，消费者零负担）：</para>
    /// <para>1) <c>Enabled=false</c>——动作类查询（按钮/轴/向量）一律返回默认值（硬门控）；</para>
    /// <para>2) 玩家压制（锁定/模态/禁用）——玩家上下文 Map（默认 Player）整体禁用；</para>
    /// <para>3) UI 压制（PreventInteractionUI/禁用）——UI 上下文 Map（默认 UI）整体禁用。</para>
    /// <para>模态打开时玩家 Map 断开而 UI Map 保持可用，模态自身热键不受影响；未列入两类 Map 的动作
    /// 不受上下文压制（仅受 Enabled 全局门控）。鼠标查询不参与门控（无源设备本就降级为默认值）。</para>
    /// </summary>
    [Serializable]
    internal sealed class UnityInputSystemHandler : InputServiceHandler
    {
        [Tooltip("留空使用 Edit > Project Settings > Input System Package 中的设置。")]
        [SerializeField] internal InputActionAsset m_InputActions;

        [Header("上下文压制 [Context Suppression]")]
        [Tooltip("玩家上下文动作 Map 名——玩家压制（锁定/模态/禁用）时整体禁用。")]
        [SerializeField] private string[] m_PlayerActionMaps = { "Player" };

        [Tooltip("UI 上下文动作 Map 名——UI 压制（PreventInteractionUI/禁用）时整体禁用。")]
        [SerializeField] private string[] m_UIActionMaps = { "UI" };

        public InputActionAsset InputActions => m_InputActions ?? InputSystem.actions;

        // 状态组合语义（Enabled/Lock/PreventUI/UIModal）——组合持有，压制态自动重置设备输入
        private readonly InputStateMachine _state = new InputStateMachine();

        // 缓存 InputAction 引用提升性能——值类型组合键（InputActionKey），查询热路径零分配；
        // 值类型不匹配负缓存按条目内标志位记录（动作+读取类型只探测/告警一次，之后直接降级）
        private sealed class CachedAction
        {
            public InputAction Action;              // null = 未找到负缓存
            public bool FloatMismatch;
            public bool Vector2Mismatch;
        }

        private readonly Dictionary<InputActionKey, CachedAction> _actionCache = new Dictionary<InputActionKey, CachedAction>();

        // 上下文 Map 名集合（OnInit 从序列化数组重建）
        private readonly HashSet<string> _playerMapNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _uiMapNames = new HashSet<string>(StringComparer.Ordinal);

        // 当前构建缓存所依据的动作资产引用——运行期资产被替换（Addressables 热更/设置变更）时重建缓存，使负缓存可恢复
        private InputActionAsset _boundActions;

        // 资产由本处理器启用（而非项目级资产自动启用）——OnShutdown/换绑时负责对称关闭
        private bool _assetEnabledByUs;

        public override bool Enabled
        {
            get => _state.Enabled;
            set => _state.Enabled = value;
        }

        public override bool LockPlayerController
        {
            get => _state.LockPlayerController;
            set => _state.LockPlayerController = value;
        }

        public override bool PreventInteractionUI
        {
            get => _state.PreventInteractionUI;
            set => _state.PreventInteractionUI = value;
        }

        internal override void SetUIModal(bool hasModal)
        {
            _state.SetUIModal(hasModal);
        }

        protected override void OnInit()
        {
            _state.ResetRequested += ResetAllInputStates;
            _state.SuppressionChanged += ApplySuppressionState;
            _actionCache.Clear();
            RebuildMapNameSets();

            var asset = InputActions;
            if (asset == null)
            {
                LogUtility.Error("Please set Input Actions in {0} or 'Project Settings -> Input System Package'", nameof(InputServiceSettings));
                return;
            }

            // 自定义资产不会自动启用（仅项目级 InputSystem.actions 由 Player Loop 启用）——此处接管并跟踪所有权
            EnsureAssetEnabled(asset);

            for (int i = 0; i < asset.actionMaps.Count; i++)
            {
                InputActionMap actionMap = asset.actionMaps[i];
                for (int j = 0; j < actionMap.actions.Count; j++)
                {
                    InputAction action = actionMap.actions[j];
                    _actionCache.Add(new InputActionKey(actionMap.name, action.name), new CachedAction { Action = action });
                }
            }

            _boundActions = asset;
            ApplySuppressionState();
        }

        protected override void OnShutdown()
        {
            _state.ResetRequested -= ResetAllInputStates;
            _state.SuppressionChanged -= ApplySuppressionState;
            _actionCache.Clear();

            ReleaseAssetIfOwned();
            _boundActions = null;
        }

        public override bool GetButtonDown(string actionName, string actionGroup = "")
        {
            bool output = false;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.WasPressedThisFrame();
            
            return output;
        }

        public override bool GetButtonUp(string actionName, string actionGroup = "")
        {
            bool output = false;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.WasReleasedThisFrame();
            
            return output;
        }
        
        public override bool GetBool(string actionName, string actionGroup = "")
        {
            bool output = false;
            
            var action = GetInputAction(actionGroup, actionName);
            if (action != null) output = action.IsPressed();
            
            return output;
        }

        public override float GetFloat(string actionName, string actionGroup = "")
        {
            var entry = GetCachedAction(actionGroup, actionName);
            var action = entry?.Action;
            if (action == null || entry.FloatMismatch) return 0f;

            try
            {
                return action.ReadValue<float>();
            }
            catch (ArgumentException)
            {
                // 动作值类型与 float 不兼容（如配置为 Vector2）——负缓存后不再逐帧 throw
                entry.FloatMismatch = true;
                LogTypeMismatchWarning(actionGroup, actionName, action, "float");
                return 0f;
            }
        }

        public override Vector2 GetVector2(string actionName, string actionGroup = "")
        {
            var entry = GetCachedAction(actionGroup, actionName);
            var action = entry?.Action;
            if (action == null || entry.Vector2Mismatch) return Vector2.zero;

            try
            {
                return action.ReadValue<Vector2>();
            }
            catch (ArgumentException)
            {
                entry.Vector2Mismatch = true;
                LogTypeMismatchWarning(actionGroup, actionName, action, "Vector2");
                return Vector2.zero;
            }
        }

        public override bool GetMouseButtonDown(EMouseButton button)
        {
            return GetMouseButtonControl(button)?.wasPressedThisFrame ?? false;
        }

        public override bool GetMouseButtonUp(EMouseButton button)
        {
            return GetMouseButtonControl(button)?.wasReleasedThisFrame ?? false;
        }

        public override bool GetMouseButtonPressed(EMouseButton button)
        {
            return GetMouseButtonControl(button)?.isPressed ?? false;
        }

        public override Vector2 GetMousePosition()
        {
            // Mouse.current 在无鼠标设备（掌机/部分主机/移动端）上为 null，按降级契约返回默认值
            var mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }

        private static readonly Vector2 s_ScalingFactor = new Vector2(0.00833f, 0.00833f); // 1/120
        public override Vector2 GetScrollDelta()
        {
            var mouse = Mouse.current;
            if (mouse == null) return Vector2.zero;

            // 新输入系统的 scroll 返回的是 tick（刻度），每滚一格通常是 120
            // 除以 120 是为了与旧系统值范围相似
            return Vector2.Scale(mouse.scroll.ReadValue(), s_ScalingFactor);
        }

        /// <summary>
        /// 获取指定鼠标按键的控件；无鼠标设备时返回 null（由调用方按降级契约兜底）。
        /// </summary>
        private static ButtonControl GetMouseButtonControl(EMouseButton button)
        {
            var mouse = Mouse.current;
            if (mouse == null) return null;

            switch (button)
            {
                case EMouseButton.Middle:
                    return mouse.middleButton;
                case EMouseButton.Right:
                    return mouse.rightButton;
                default:
                    return mouse.leftButton;
            }
        }

        public override void ResetAllInputStates()
        {
            InputSystem.FlushDisconnectedDevices();
            foreach (var device in InputSystem.devices)
            {
                if (device.added) InputSystem.ResetDevice(device);
            }
        }

        private InputAction GetInputAction(string actionGroup, string actionName)
        {
            return GetCachedAction(actionGroup, actionName)?.Action;
        }

        /// <summary>
        /// 解析动作缓存条目（全局 Enabled 硬门控 + 上下文压制门控在此收敛）。
        /// <para>返回 null 表示查询降级（未启用/被压制/未找到/资产缺失）；返回条目的
        /// <see cref="CachedAction.Action"/> 为 null 表示未找到负缓存。</para>
        /// </summary>
        private CachedAction GetCachedAction(string actionGroup, string actionName)
        {
            // 全局硬门控：未启用时动作类查询一律降级（鼠标查询不经此路径，不受门控）
            if (!_state.Enabled) return null;

            var asset = InputActions;
            if (asset == null)
            {
                // OnInit 已报错，此处按降级契约静默返回默认值
                return null;
            }

            // 资产引用变化（运行期替换/延迟赋值）时重建缓存，使此前的负缓存（未找到/类型不匹配）可恢复
            if (!ReferenceEquals(asset, _boundActions))
            {
                ReleaseAssetIfOwned();
                _actionCache.Clear();
                _boundActions = asset;
                EnsureAssetEnabled(asset);
                ApplySuppressionState();
            }

            var key = new InputActionKey(actionGroup, actionName);
            if (!_actionCache.TryGetValue(key, out CachedAction entry))
            {
                // 仅未命中路径合成全限定名（每动作一次，非热路径）
                string fullActionName = key.ToFullName();
                var action = asset.FindAction(fullActionName);
                entry = new CachedAction { Action = action };
                _actionCache.Add(key, entry);

                if (action != null)
                {
                    if (!IsActionSuppressed(action)) action.Enable();
                }
                else
                {
                    LogUtility.Warning($"Action '{fullActionName}' not found! " +
                                     "Please check Input Action Asset configuration.");
                }
                // Debug.Log($"GetInputAction: {fullActionName} - {action}");
            }

            // 上下文压制门控：被压制 Map 的动作查询降级（Map 已被整体禁用，此处为兜底与即时生效）
            if (entry.Action != null && IsActionSuppressed(entry.Action)) return null;

            return entry;
        }

        /// <summary>
        /// 按当前有效压制态启用/禁用上下文 Map（<see cref="InputStateMachine.SuppressionChanged"/> 驱动，OnInit 末尾对齐一次）。
        /// <para>未列入两类 Map 的动作不受影响；Map 禁用后其全部动作（含尚未缓存的）查询自然降级。</para>
        /// </summary>
        private void ApplySuppressionState()
        {
            var asset = _boundActions;
            if (asset == null) return;

            bool playerSuppressed = _state.IsPlayerInputSuppressed;
            bool uiSuppressed = _state.IsUIInteractionSuppressed;

            for (int i = 0; i < asset.actionMaps.Count; i++)
            {
                var map = asset.actionMaps[i];
                if (_playerMapNames.Contains(map.name))
                {
                    if (playerSuppressed) map.Disable();
                    else map.Enable();
                }
                else if (_uiMapNames.Contains(map.name))
                {
                    if (uiSuppressed) map.Disable();
                    else map.Enable();
                }
            }
        }

        /// <summary>
        /// 动作当前是否处于被压制的上下文（玩家压制且属于玩家 Map / UI 压制且属于 UI Map）。
        /// </summary>
        private bool IsActionSuppressed(InputAction action)
        {
            var map = action.actionMap;
            if (map == null) return false;

            if (_playerMapNames.Contains(map.name)) return _state.IsPlayerInputSuppressed;
            if (_uiMapNames.Contains(map.name)) return _state.IsUIInteractionSuppressed;
            return false;
        }

        /// <summary>
        /// 自定义资产未启用时接管启用（项目级资产已由 Player Loop 启用，不重复接管）。
        /// </summary>
        private void EnsureAssetEnabled(InputActionAsset asset)
        {
            if (asset.enabled) return;

            asset.Enable();
            _assetEnabledByUs = true;
        }

        /// <summary>
        /// 换绑/关闭时对称释放由本处理器启用的资产。
        /// </summary>
        private void ReleaseAssetIfOwned()
        {
            if (!_assetEnabledByUs || _boundActions == null) return;

            _boundActions.Disable();
            _assetEnabledByUs = false;
        }

        /// <summary>
        /// 从序列化数组重建上下文 Map 名集合（空名跳过，防止误匹配未命名 Map）。
        /// </summary>
        private void RebuildMapNameSets()
        {
            _playerMapNames.Clear();
            _uiMapNames.Clear();

            if (m_PlayerActionMaps != null)
            {
                for (int i = 0; i < m_PlayerActionMaps.Length; i++)
                {
                    if (!string.IsNullOrEmpty(m_PlayerActionMaps[i])) _playerMapNames.Add(m_PlayerActionMaps[i]);
                }
            }

            if (m_UIActionMaps != null)
            {
                for (int i = 0; i < m_UIActionMaps.Length; i++)
                {
                    if (!string.IsNullOrEmpty(m_UIActionMaps[i])) _uiMapNames.Add(m_UIActionMaps[i]);
                }
            }
        }

        /// <summary>
        /// 类型不匹配告警（仅首次探测到不匹配时调用一次，非热路径）。
        /// </summary>
        private static void LogTypeMismatchWarning(string actionGroup, string actionName, InputAction action, string readType)
        {
            string fullActionName = new InputActionKey(actionGroup, actionName).ToFullName();
            LogUtility.Warning($"Action '{fullActionName}' value type is not compatible with {readType}. " +
                             $"Expected control type: '{action.expectedControlType}'. Returning default value.");
        }
    }
}
#endif