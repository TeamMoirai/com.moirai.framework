using System;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 基于 Unity IMGUI 内置调试器窗口组的默认调试器处理器。
    /// <para><see cref="DebuggerServiceHandler"/> 的内置实现，管理调试器窗口组的注册、选择与激活。</para>
    /// </summary>
    [Serializable]
    public sealed class DefaultDebuggerHandler : DebuggerServiceHandler
    {
        [NonSerialized] private DebuggerService.DebuggerWindowGroup _debuggerWindowRoot;
        [NonSerialized] private bool _activeWindow;

        /// <summary>
        /// 获取或设置调试器窗口是否激活。
        /// </summary>
        public override bool ActiveWindow
        {
            get => _activeWindow;
            set => _activeWindow = value;
        }

        /// <summary>
        /// 调试器窗口根结点。
        /// </summary>
        public override IDebuggerWindowGroup DebuggerWindowRoot => _debuggerWindowRoot;

        protected override void OnInit()
        {
            _debuggerWindowRoot = new DebuggerService.DebuggerWindowGroup();
            _activeWindow = false;
        }

        protected override void OnShutdown()
        {
            _activeWindow = false;
            _debuggerWindowRoot.Shutdown();
            _debuggerWindowRoot = null;
        }

        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            if (!_activeWindow)
            {
                return;
            }

            _debuggerWindowRoot.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="debuggerWindow">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public override void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow, params object[] args)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new GameException("Path is invalid.");
            }

            if (debuggerWindow == null)
            {
                throw new GameException("Debugger window is invalid.");
            }

            _debuggerWindowRoot.RegisterDebuggerWindow(path, debuggerWindow);
            debuggerWindow.Initialize(args);
        }

        /// <summary>
        /// 解除注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否解除注册调试器窗口成功。</returns>
        public override bool UnregisterDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.UnregisterDebuggerWindow(path);
        }

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>要获取的调试器窗口。</returns>
        public override IDebuggerWindow GetDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.GetDebuggerWindow(path);
        }

        /// <summary>
        /// 选中调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否成功选中调试器窗口。</returns>
        public override bool SelectDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.SelectDebuggerWindow(path);
        }
    }
}
