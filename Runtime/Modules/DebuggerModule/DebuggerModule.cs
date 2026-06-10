namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器管理器。
    /// </summary>
    internal sealed partial class DebuggerModule : Module, IDebuggerModule, IUpdateModule
    {
        private DebuggerWindowGroup _debuggerWindowRoot;
        private bool _activeWindow;
        
        public override int Priority => -1;

        /// <summary>
        /// 获取或设置调试器窗口是否激活。
        /// </summary>
        public bool ActiveWindow
        {
            get => _activeWindow;
            set => _activeWindow = value;
        }
        
        /// <summary>
        /// 调试器窗口根结点。
        /// </summary>
        public IDebuggerWindowGroup DebuggerWindowRoot => _debuggerWindowRoot;
        
        public override void OnInit()
        {
            _debuggerWindowRoot = new DebuggerWindowGroup();
            _activeWindow = false;
        }
        
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (!_activeWindow)
            {
                return;
            }

            _debuggerWindowRoot.OnUpdate(elapseSeconds, realElapseSeconds);
        }
        
        public override void Shutdown()
        {
            _activeWindow = false;
            _debuggerWindowRoot.Shutdown();
        }

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="debuggerWindow">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow, params object[] args)
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
        public bool UnregisterDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.UnregisterDebuggerWindow(path);
        }

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>要获取的调试器窗口。</returns>
        public IDebuggerWindow GetDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.GetDebuggerWindow(path);
        }

        /// <summary>
        /// 选中调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否成功选中调试器窗口。</returns>
        public bool SelectDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.SelectDebuggerWindow(path);
        }
    }
}
