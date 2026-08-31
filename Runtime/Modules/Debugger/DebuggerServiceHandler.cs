using System;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器服务配置抽象基类（纯数据，无行为无生命周期）。
    /// <para>以 <see cref="UnityEngine.SerializeReference"/> 存于 <see cref="DebuggerServiceSettings"/> 资产；
    /// 经 <see cref="CreateHandler"/> 工厂创建绑定的后端处理器实例，处理器不再被序列化。</para>
    /// </summary>
    [Serializable]
    public abstract class DebuggerServiceHandlerConfig
    {
        /// <summary>
        /// 创建配置绑定的调试器后端处理器实例。
        /// </summary>
        /// <returns>新的调试器处理器实例。</returns>
        public abstract DebuggerServiceHandler CreateHandler();
    }

    /// <summary>
    /// 调试器处理器抽象基类（策略模式抽象策略）。定义 <see cref="DebuggerService"/> 外观调用的调试器后端契约。
    /// <para>默认实现为 <see cref="DefaultDebuggerHandler"/>（UI Toolkit 运行时调试器），可在 <see cref="DebuggerServiceSettings"/> 中替换为自定义实现。</para>
    /// <para>配置数据由 <see cref="DebuggerServiceHandlerConfig"/> 系列纯数据类承载——处理器实例本身不再被序列化，由 <see cref="DebuggerServiceHandlerConfig.CreateHandler"/> 工厂在运行期创建。</para>
    /// </summary>
    public abstract class DebuggerServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 获取或设置调试器是否激活（悬浮入口可见；关闭时零 UI 开销）。
        /// </summary>
        public abstract bool ActiveWindow
        {
            get;
            set;
        }

        /// <summary>
        /// 获取或设置完整调试器窗口是否展开。
        /// </summary>
        public abstract bool ShowFullWindow
        {
            get;
            set;
        }

        /// <summary>
        /// 获取调试器窗口注册表（路径树导航模型）。
        /// </summary>
        public abstract DebuggerWindowRegistry WindowRegistry
        {
            get;
        }

        /// <summary>
        /// 获取日志捕获器（环形缓冲，供控制台与外部工具消费）。
        /// </summary>
        public abstract DebuggerLogCapture LogCapture
        {
            get;
        }

        /// <summary>
        /// 容器 Tick 驱动——排空日志捕获并轮询可见窗口。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="window">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public abstract void RegisterDebuggerWindow(string path, IDebuggerWindow window, params object[] args);

        /// <summary>
        /// 解除注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否解除注册调试器窗口成功。</returns>
        public abstract bool UnregisterDebuggerWindow(string path);

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>要获取的调试器窗口。</returns>
        public abstract IDebuggerWindow GetDebuggerWindow(string path);

        /// <summary>
        /// 选中调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否成功选中调试器窗口。</returns>
        public abstract bool SelectDebuggerWindow(string path);
    }
}
