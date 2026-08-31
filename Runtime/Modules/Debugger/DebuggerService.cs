namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器服务外观（Facade）。
    /// <para>统一的静态调试器访问入口，通过替换 <see cref="Handler"/> 即可切换调试器后端。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="DebuggerServiceSettings"/> 创建处理器实例。</para>
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
            return DebuggerServiceSettings.DebuggerServiceHandlerConfig.CreateHandler();
        }

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
        /// <para>先摘除 Handler 引用再关闭——窗口组关闭回调抛异常时不得让半关状态的 Handler 残留，
        /// 否则下一次懒加载会向旧窗口组重复注册（"Debugger window has been registered"）。</para>
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            handler?.Internal_Shutdown();
        }

        /// <summary>
        /// 容器 Tick 驱动——转发到处理器更新窗口组。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds) =>
            s_Handler?.Tick(elapseSeconds, realElapseSeconds);

        #endregion
        
        #region 窗口管理 [WINDOW MANAGEMENT]

        /// <summary>
        /// 获取或设置调试器窗口是否激活。
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
        /// 调试器窗口根结点。
        /// </summary>
        public static IDebuggerWindowGroup DebuggerWindowRoot => s_Handler?.DebuggerWindowRoot;

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="debuggerWindow">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public static void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow, params object[] args) =>
            s_Handler?.RegisterDebuggerWindow(path, debuggerWindow, args);

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

        #endregion
    }
}
