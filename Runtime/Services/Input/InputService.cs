using Moirai.Atropos.Events;
using Moirai.Atropos.UI;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入服务外观（Facade）。
    /// <para>统一的静态输入访问入口，通过替换 <see cref="Handler"/> 即可在不同输入后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="InputServiceSettings"/> 解析；settings 未配置则回退 <see cref="CreateDefaultHandler"/>。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// <para>降级契约：全部外观 API 经 <c>s_Handler?.</c> 静默降级（未注册/未初始化时返回安全默认值），
    /// 与 Audio/Resource 等服务一致。</para>
    /// </summary>
    // 依赖说明：经 EventManager 订阅 UIServiceEvent + 读 UIService.CurrentModal——事件驱动软依赖，
    // 不做 [ServiceDependency] 硬声明（UI 侧对 Input 是静态调用硬依赖，双向硬声明会构成拓扑环）。
    [HandlerHost(typeof(InputServiceHandler))]
    public partial class InputService : ServiceBase
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认输入处理器（settings 未配置时的代码兜底，按编译符号选择输入后端）。
        /// </summary>
        /// <returns>默认输入处理器实例；无可用输入后端时返回 <c>null</c>。</returns>
        internal static InputServiceHandler CreateDefaultHandler()
        {
#if ENABLE_INPUT_SYSTEM
            return new UnityInputSystemHandler();
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new UnityInputManagerHandler();
#else
            return null;
#endif
        }

        /// <summary>
        /// 从 <see cref="InputServiceSettings"/> 解析输入处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——懒加载主路径（settings 已配置时 <see cref="CreateDefaultHandler"/> 被短路）首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static InputServiceHandler GetHandlerFromSettings()
        {
            GameServices.EnsureRegistered<InputService>();
            return InputServiceSettings.InputServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化输入服务。由 <see cref="GameAppSettings.Initiation"/> 调用。
        /// <para>确保 <c>InputService.Handler</c> 已赋值（触发 <c>Handler</c> 懒加载），
        /// 然后订阅全局事件。</para>
        /// </summary>
        public override void OnInit()
        {
            // 确保 Handler 已初始化
            _ = Handler;

            EventManager.RegisterCallback<GameAppMessageEvent>(ResetInput);
            EventManager.RegisterCallback<UIServiceEvent>(RefreshUIModal);
        }

        /// <summary>
        /// 关闭输入服务。由 <see cref="GameApp.Shutdown"/> 调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();

            EventManager.UnregisterCallback<GameAppMessageEvent>(ResetInput);
            EventManager.UnregisterCallback<UIServiceEvent>(RefreshUIModal);
        }

        #endregion

        #region 状态管理 [STATE MANAGEMENT]

        /// <summary>
        /// 获取或设置是否启用输入（未就绪时读取为 false，写入静默忽略）。
        /// <para>禁用 = 全局硬门控：动作类查询（按钮/轴/向量）一律返回默认值，无需消费者自查；
        /// Input System 后端同时整体禁用全部上下文 Map。鼠标查询不参与门控。</para>
        /// </summary>
        public static bool Enabled
        {
            get => s_Handler?.Enabled ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.Enabled = value;
            }
        }

        /// <summary>
        /// 获取或设置是否锁定玩家控制器（未就绪时读取为 false，写入静默忽略）。
        /// <para>Input System 后端中心化强制：锁定（含 UI 模态联动）时玩家上下文 Map 整体禁用，
        /// 玩家输入查询返回默认值而 UI Map 保持可用；旧版/移动端后端无 Map 概念，该状态仅供消费者协作自查。</para>
        /// </summary>
        public static bool LockPlayerController
        {
            get => s_Handler?.LockPlayerController ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.LockPlayerController = value;
            }
        }

        /// <summary>
        /// 获取或设置是否禁止 UI 交互（未就绪时读取为 false，写入静默忽略）。
        /// <para>Input System 后端中心化强制：禁止时 UI 上下文 Map 整体禁用；
        /// UI 侧交互（UIServiceHelper/UIHotKey 等）亦会自查该状态，双保险。</para>
        /// </summary>
        public static bool PreventInteractionUI
        {
            get => s_Handler?.PreventInteractionUI ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.PreventInteractionUI = value;
            }
        }

        /// <summary>
        /// 获取当前输入处理器
        /// </summary>
        public static InputServiceHandler CurrentHandler => s_Handler;

        #endregion

        #region 输入查询 [INPUT QUERIES]

        /// <summary>
        /// 按钮是否被按下
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按下（未就绪时为 false）</returns>
        public static bool GetButtonDown(string actionName, string actionGroup = "") =>
            s_Handler?.GetButtonDown(actionName, actionGroup) ?? false;

        /// <summary>
        /// 按钮是否被松开
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否抬起（未就绪时为 false）</returns>
        public static bool GetButtonUp(string actionName, string actionGroup = "") =>
            s_Handler?.GetButtonUp(actionName, actionGroup) ?? false;

        /// <summary>
        /// 按钮是否被按住（<see cref="GetBool"/> 的别名，保留以贴近旧版 Input 习惯命名）。
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按住（未就绪时为 false）</returns>
        public static bool GetButtonPressed(string actionName, string actionGroup = "") =>
            GetBool(actionName, actionGroup);

        /// <summary>
        /// 按钮是否被按住（<see cref="GetBool"/> 的别名，保留以贴近旧版 Input 习惯命名）。
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按住（未就绪时为 false）</returns>
        public static bool GetButton(string actionName, string actionGroup = "") =>
            GetBool(actionName, actionGroup);

        /// <summary>
        /// 获取指定输入动作的 bool
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>按钮状态布尔值（未就绪时为 false）。</returns>
        public static bool GetBool(string actionName, string actionGroup = "") =>
            s_Handler?.GetBool(actionName, actionGroup) ?? false;

        /// <summary>
        /// 获取指定输入动作的 float
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>返回驱动此动作的控件或绑定的当前值（未就绪时为 0）。</returns>
        public static float GetFloat(string actionName, string actionGroup = "") =>
            s_Handler?.GetFloat(actionName, actionGroup) ?? 0f;

        /// <summary>
        /// 获取指定输入动作的 Vector2
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>返回驱动此动作的控件或绑定的当前值（未就绪时为 zero）。</returns>
        public static Vector2 GetVector2(string actionName, string actionGroup = "") =>
            s_Handler?.GetVector2(actionName, actionGroup) ?? Vector2.zero;

        /// <summary>
        /// 获取是否按下指定鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否在本帧按下（未就绪时为 false）。</returns>
        public static bool GetMouseButtonDown(EMouseButton button) =>
            s_Handler?.GetMouseButtonDown(button) ?? false;

        /// <summary>
        /// 获取是否抬起指定鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否在本帧抬起（未就绪时为 false）。</returns>
        public static bool GetMouseButtonUp(EMouseButton button) =>
            s_Handler?.GetMouseButtonUp(button) ?? false;

        /// <summary>
        /// 获取是否按住指定鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否正在按住（未就绪时为 false）。</returns>
        public static bool GetMouseButtonPressed(EMouseButton button) =>
            s_Handler?.GetMouseButtonPressed(button) ?? false;

        /// <summary>
        /// 返回鼠标的当前位置
        /// </summary>
        /// <returns>鼠标屏幕坐标（未就绪时为 zero）。</returns>
        public static Vector2 GetMousePosition() =>
            s_Handler?.GetMousePosition() ?? Vector2.zero;

        /// <summary>
        /// 获取鼠标滚轮滚动值
        /// </summary>
        /// <returns>滚轮滚动增量（未就绪时为 zero）。</returns>
        public static Vector2 GetScrollDelta() =>
            s_Handler?.GetScrollDelta() ?? Vector2.zero;

        #endregion

        #region 事件 [EVENTS]

        // 失焦/回焦联动：重复焦点事件去重，避免连续失焦把记录值覆盖为 false 导致回焦后输入永久关闭
        private static readonly FocusInputGuard s_FocusGuard = new FocusInputGuard();

        private static void ResetInput(GameAppMessageEvent evt)
        {
            var handler = s_Handler;
            if (handler == null) return;

            bool hasFocus;
            switch (evt.EventType)
            {
                case GameAppMessageEvent.EEventType.NotApplicationFocus:
                    hasFocus = false;
                    break;

                case GameAppMessageEvent.EEventType.ApplicationFocus:
                    hasFocus = true;
                    break;

                default:
                    return;
            }

            bool? target = s_FocusGuard.Evaluate(hasFocus, handler.Enabled);
            if (target.HasValue) handler.Enabled = target.Value;
        }

        private static void RefreshUIModal(UIServiceEvent evt)
        {
            if (s_Handler == null) return;

            if (evt.Mode == UIServiceEvent.EMode.Shown || evt.Mode == UIServiceEvent.EMode.Closed)
            {
                s_Handler.SetUIModal(UIService.CurrentModal != null);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器禁用 Domain Reload 的 Enter Play Mode 设置下重置静态字段——失焦记录不得跨 Play 会话残留。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForDomainReloadDisabled()
        {
            s_FocusGuard.Reset();
        }
#endif

        #endregion
    }
}
