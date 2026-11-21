namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器服务契约（策略模式契约接口）：调试器后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 默认实现为 <see cref="DefaultDebuggerHandler"/>（UI Toolkit 运行时调试器），可在 <see cref="DebuggerServiceSettings"/> 中替换。 <br />
    /// 实现约束：实现类须继承 <see cref="Moirai.Atropos.FrameworkHandler"/>（生命周期与序列化基座）。
    /// </remarks>
    public interface IDebuggerService
    {
        /// <summary>获取或设置调试器是否激活（悬浮入口可见；关闭时零 UI 开销）。</summary>
        bool ActiveWindow { get; set; }

        /// <summary>获取或设置完整调试器窗口是否展开。</summary>
        bool ShowFullWindow { get; set; }

        /// <summary>获取调试器窗口注册表（路径树导航模型）。</summary>
        DebuggerWindowRegistry WindowRegistry { get; }

        /// <summary>获取日志捕获器（环形缓冲，供控制台与外部工具消费）。</summary>
        DebuggerLogCapture LogCapture { get; }

        /// <summary>
        /// 容器 Tick 驱动——排空日志捕获并轮询可见窗口。
        /// </summary>
        void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="args">初始化调试器窗口参数。</param>
        void RegisterDebuggerWindow(string path, IDebuggerWindow window, params object[] args);

        /// <summary>
        /// 解除注册调试器窗口。
        /// </summary>
        /// <returns>是否解除注册调试器窗口成功。</returns>
        bool UnregisterDebuggerWindow(string path);

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        IDebuggerWindow GetDebuggerWindow(string path);

        /// <summary>
        /// 选中调试器窗口。
        /// </summary>
        /// <returns>是否成功选中调试器窗口。</returns>
        bool SelectDebuggerWindow(string path);
    }
}
