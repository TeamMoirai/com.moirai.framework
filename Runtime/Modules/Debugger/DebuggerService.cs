using System.Collections.Generic;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器服务外观（Facade）。
    /// <para>统一的静态调试器访问入口：窗口注册/检索/选中、激活开关、日志检索与自定义面板注册。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="DebuggerServiceSettings"/> 创建处理器实例；外观方法经 <c>s_Handler</c> 直接转发（未注册时静默降级为默认值——仅主动注册方可使用服务）。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(DebuggerServiceHandler))]
    public partial class DebuggerService : ServiceBase, IServiceTickable
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 从 <see cref="DebuggerServiceSettings"/> 创建默认调试器处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认调试器处理器实例。</returns>
        private static DebuggerServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<DebuggerService>();
            return DebuggerServiceSettings.DebuggerServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => -1;

        /// <summary>
        /// 初始化调试器服务。由容器在构建期调用。
        /// <para>确保 <c>DebuggerService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭调试器服务。由容器在关闭期调用。
        /// <para>先摘除 Handler 引用再关闭——窗口关闭回调抛异常时不得让半关状态的 Handler 残留，
        /// 否则下一次懒加载会向旧注册表重复注册窗口。</para>
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器（排空日志并轮询可见窗口）。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        #endregion

        #region 状态 [STATE]

        /// <summary>
        /// 获取或设置调试器是否激活（悬浮入口可见）。
        /// </summary>
        public static bool ActiveWindow
        {
            get => s_Handler?.ActiveWindow ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.ActiveWindow = value;
            }
        }

        /// <summary>
        /// 获取或设置完整调试器窗口是否展开。
        /// </summary>
        public static bool ShowFullWindow
        {
            get => s_Handler?.ShowFullWindow ?? false;
            set
            {
                if (s_Handler == null) return;
                s_Handler.ShowFullWindow = value;
            }
        }

        /// <summary>
        /// 获取调试器激活策略（直接读自 <see cref="DebuggerServiceSettings"/>，不依赖服务注册状态）。
        /// </summary>
        public static DebuggerActiveWindowType ActiveWindowType => DebuggerServiceSettings.ActiveWindowType;

        /// <summary>
        /// 获取调试器窗口注册表（路径树导航模型；服务未注册时为 null）。
        /// </summary>
        public static DebuggerWindowRegistry WindowRegistry => s_Handler?.WindowRegistry;

        /// <summary>
        /// 获取日志捕获器（服务未注册时为 null）。
        /// </summary>
        public static DebuggerLogCapture LogCapture => s_Handler?.LogCapture;

        #endregion

        #region 窗口管理 [WINDOW MANAGEMENT]

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径（如 "Game/Player"）。</param>
        /// <param name="window">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public static void RegisterDebuggerWindow(string path, IDebuggerWindow window, params object[] args) =>
            s_Handler?.RegisterDebuggerWindow(path, window, args);

        /// <summary>
        /// 解除注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否解除注册调试器窗口成功。</returns>
        public static bool UnregisterDebuggerWindow(string path) =>
            s_Handler?.UnregisterDebuggerWindow(path) ?? true;

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>要获取的调试器窗口。</returns>
        public static IDebuggerWindow GetDebuggerWindow(string path) =>
            s_Handler?.GetDebuggerWindow(path);

        /// <summary>
        /// 选中调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否成功选中调试器窗口。</returns>
        public static bool SelectDebuggerWindow(string path) =>
            s_Handler?.SelectDebuggerWindow(path) ?? false;

        /// <summary>
        /// 以流式构建器注册自定义调试面板。
        /// <para>一行注册：滑条、开关、按钮、折叠组等控件经 <see cref="DebugPanelBuilder"/> 声明，窗口标题取路径末段。</para>
        /// </summary>
        /// <param name="path">调试器窗口路径（如 "Game/Player"）。</param>
        /// <param name="configure">面板构建回调。</param>
        public static void RegisterPanel(string path, System.Action<DebugPanelBuilder> configure)
        {
            if (configure == null)
            {
                throw new GameException("Panel configure delegate is invalid.");
            }

            s_Handler?.RegisterDebuggerWindow(path, new DebugPanel(path), configure);
        }

        /// <summary>
        /// 注册服务调试视图（<see cref="ServiceDebugView"/> 的 IMGUI 内容经适配器嵌入 UI Toolkit 调试器）。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="view">要注册的服务调试视图。</param>
        public static void RegisterDebugView(string path, ServiceDebugView view)
        {
            if (view == null)
            {
                throw new GameException("Service debug view is invalid.");
            }

            s_Handler?.RegisterDebuggerWindow(path, new IMGUIDebuggerWindow(view));
        }

        #endregion

        #region 日志检索 [LOG QUERY]

        /// <summary>
        /// 获取记录的所有日志。
        /// </summary>
        /// <param name="results">要获取的日志。</param>
        public static void GetRecentLogs(List<LogNode> results) =>
            s_Handler?.LogCapture.GetRecentLogs(results);

        /// <summary>
        /// 获取记录的最近日志。
        /// </summary>
        /// <param name="results">要获取的日志。</param>
        /// <param name="count">要获取最近日志的数量。</param>
        public static void GetRecentLogs(List<LogNode> results, int count) =>
            s_Handler?.LogCapture.GetRecentLogs(results, count);

        #endregion
    }
}
