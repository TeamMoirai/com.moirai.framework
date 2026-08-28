using System;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器处理器抽象基类（策略模式抽象策略）。定义 <see cref="DebuggerService"/> 外观调用的调试器后端契约。
    /// <para>默认实现为 <see cref="DefaultDebuggerHandler"/>（内置调试器窗口组），可在 <see cref="DebuggerServiceSettings"/> 中替换为自定义实现。</para>
    /// </summary>
    [Serializable]
    public abstract class DebuggerServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 获取或设置调试器窗口是否激活。
        /// </summary>
        public abstract bool ActiveWindow { get; set; }

        /// <summary>
        /// 调试器窗口根结点。
        /// </summary>
        public abstract IDebuggerWindowGroup DebuggerWindowRoot { get; }

        /// <summary>
        /// 容器 Tick 驱动——轮询激活的调试器窗口。
        /// </summary>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="debuggerWindow">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public abstract void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow, params object[] args);

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
