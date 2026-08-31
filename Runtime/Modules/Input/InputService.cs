using Moirai.Atropos.Events;
using Moirai.Atropos.UI;
using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 输入服务外观（Facade）。
    /// <para>统一的静态输入访问入口，通过替换 <see cref="Handler"/> 即可在不同输入后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="InputServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(InputServiceHandler))]
    public partial class InputService : ServiceBase
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="InputServiceSettings"/> 创建默认输入处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认输入处理器实例。</returns>
        private static InputServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<InputService>();
            return InputServiceSettings.InputServiceHandlerConfig.CreateHandler();
        }

        /// <summary>
        /// 初始化输入服务。由 <see cref="GameAppSettings.Initiation"/> 调用。
        /// <para>确保 <c>InputService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载），
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

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 状态管理 [STATE MANAGEMENT]

        /// <summary>
        /// 获取或设置是否启用输入。
        /// </summary>
        public static bool Enabled
        {
            get => Handler.Enabled;
            set
            {
                Handler.Enabled = value;
            }
        }

        /// <summary>
        /// 获取或设置是否锁定玩家控制器。
        /// </summary>
        public static bool LockPlayerController
        {
            get => Handler.LockPlayerController;
            set
            {
                Handler.LockPlayerController = value;
            }
        }

        /// <summary>
        /// 获取或设置是否禁止 UI 交互。
        /// </summary>
        public static bool PreventInteractionUI
        {
            get => Handler.PreventInteractionUI;
            set
            {
                Handler.PreventInteractionUI = value;
            }
        }

        #endregion

        #region 输入查询 [INPUT QUERIES]

        /// <summary>
        /// 按钮是否被按下
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按下</returns>
        public static bool GetButtonDown(string actionName, string actionGroup = "") =>
            Handler.GetButtonDown(actionName, actionGroup);

        /// <summary>
        /// 按钮是否被松开
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否抬起</returns>
        public static bool GetButtonUp(string actionName, string actionGroup = "") =>
            Handler.GetButtonUp(actionName, actionGroup);

        /// <summary>
        /// 按钮是否被按住
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按住</returns>
        public static bool GetButtonPressed(string actionName, string actionGroup = "") =>
            GetBool(actionName, actionGroup);

        /// <summary>
        /// 按钮是否被按住
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>是否按住</returns>
        public static bool GetButton(string actionName, string actionGroup = "") =>
            GetBool(actionName, actionGroup);

        /// <summary>
        /// 获取指定输入动作的 bool
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>按钮状态布尔值。</returns>
        public static bool GetBool(string actionName, string actionGroup = "") =>
            Handler.GetBool(actionName, actionGroup);

        /// <summary>
        /// 获取指定输入动作的 float
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>返回驱动此动作的控件或绑定的当前值。</returns>
        public static float GetFloat(string actionName, string actionGroup = "") =>
            Handler.GetFloat(actionName, actionGroup);

        /// <summary>
        /// 获取指定输入动作的 Vector2
        /// </summary>
        /// <param name="actionName">输入动作名，如果为全称则 actionGroup 置空</param>
        /// <param name="actionGroup">输入动作分组</param>
        /// <returns>返回驱动此动作的控件或绑定的当前值。</returns>
        public static Vector2 GetVector2(string actionName, string actionGroup = "") =>
            Handler.GetVector2(actionName, actionGroup);

        /// <summary>
        /// 获取是否按下指定鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否在本帧按下。</returns>
        public static bool GetMouseButtonDown(EMouseButton button) =>
            Handler.GetMouseButtonDown(button);

        /// <summary>
        /// 获取是否抬起指定鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否在本帧抬起。</returns>
        public static bool GetMouseButtonUp(EMouseButton button) =>
            Handler.GetMouseButtonUp(button);

        /// <summary>
        /// 获取是否按住指定鼠标按键
        /// </summary>
        /// <param name="button">鼠标按键。</param>
        /// <returns>是否正在按住。</returns>
        public static bool GetMouseButtonPressed(EMouseButton button) =>
            Handler.GetMouseButtonPressed(button);

        /// <summary>
        /// 返回鼠标的当前位置
        /// </summary>
        /// <returns>鼠标屏幕坐标。</returns>
        public static Vector2 GetMousePosition() =>
            Handler.GetMousePosition();

        /// <summary>
        /// 获取鼠标滚轮滚动值
        /// </summary>
        /// <returns>滚轮滚动增量。</returns>
        public static Vector2 GetScrollDelta() =>
            Handler.GetScrollDelta();

        #endregion

        #region 事件 [EVENTS]

        private static void ResetInput(GameAppMessageEvent evt)
        {
            if (s_Handler == null) return;

            switch (evt.EventType)
            {
                case EMessageEventType.ApplicationFocus:
                    s_Handler.Enabled = true;
                    break;

                case EMessageEventType.NotApplicationFocus:
                    s_Handler.Enabled = false;
                    break;
            }
        }

        private static void RefreshUIModal(UIServiceEvent evt)
        {
            if (s_Handler == null) return;

            if (evt.Mode == UIServiceEvent.EMode.Shown || evt.Mode == UIServiceEvent.EMode.Closed)
            {
                s_Handler.SetUIModal(UIService.CurrentModal != null);
            }
        }

        #endregion
    }
}
