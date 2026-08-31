using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// UI Toolkit 调试器后端配置。
    /// </summary>
    [Serializable]
    public sealed class DefaultDebuggerHandlerConfig : DebuggerServiceHandlerConfig
    {
        #region 字段 [FIELDS]

        [SerializeField] private int m_ConsoleCapacity = 256;

        [SerializeField] private float m_FpsUpdateInterval = 0.5f;

        [SerializeField] private bool m_StatsOverlayVisible = false;

        [SerializeField, Range(0.2f, 1f)] private float m_WindowOpacity = 1f;

        #endregion

        #region 工厂 [FACTORY]

        /// <inheritdoc />
        public override DebuggerServiceHandler CreateHandler()
        {
            return new DefaultDebuggerHandler(this);
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取控制台环形缓冲容量。
        /// </summary>
        public int ConsoleCapacity => m_ConsoleCapacity;

        /// <summary>
        /// 获取悬浮入口与统计的帧率刷新间隔（秒）。
        /// </summary>
        public float FpsUpdateInterval => m_FpsUpdateInterval;

        /// <summary>
        /// 获取常驻统计 HUD 初始可见性。
        /// </summary>
        public bool StatsOverlayVisible => m_StatsOverlayVisible;

        /// <summary>
        /// 获取主窗口不透明度（0.2-1）。
        /// </summary>
        public float WindowOpacity => m_WindowOpacity;

        #endregion
    }

    /// <summary>
    /// 基于 UI Toolkit 运行时界面的默认调试器处理器。
    /// <para><see cref="DebuggerServiceHandler"/> 的内置实现：持有窗口注册表与日志捕获器，按激活策略解析悬浮入口可见性，并在首个 Tick 懒建运行时宿主（<see cref="DebuggerRuntimeHost"/>，纯运行时构建的 UIDocument，无资产依赖）。</para>
    /// <para>由 <see cref="DefaultDebuggerHandlerConfig"/> 工厂创建（普通运行时类，不参与序列化——运行时字段无需 [NonSerialized] 标注）。</para>
    /// </summary>
    public sealed class DefaultDebuggerHandler : DebuggerServiceHandler
    {
        #region 字段 [FIELDS]

        private readonly DefaultDebuggerHandlerConfig m_Config;
        private readonly List<IDebuggerWindow> _windowBuffer = new List<IDebuggerWindow>(32);
        private DebuggerWindowRegistry _windowRegistry;
        private DebuggerLogCapture _logCapture;
        private DebuggerRuntimeHost _host;
        private bool _activeWindow;
        private bool _showFullWindow;
        private bool _hostPending;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化默认调试器处理器的新实例。
        /// </summary>
        /// <param name="config">后端配置。</param>
        public DefaultDebuggerHandler(DefaultDebuggerHandlerConfig config)
        {
            m_Config = config;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <inheritdoc />
        public override bool ActiveWindow
        {
            get
            {
                return _activeWindow;
            }
            set
            {
                _activeWindow = value;
                if (_activeWindow)
                {
                    _hostPending = true;
                }
                else
                {
                    _showFullWindow = false;
                    if (_host != null)
                    {
                        _host.ShowFullWindow = false;
                    }
                }

                _host?.SetRootVisible(_activeWindow);
            }
        }

        /// <inheritdoc />
        public override bool ShowFullWindow
        {
            get
            {
                // 宿主存在时以其为唯一事实源：悬浮入口点击、标题栏关闭等 UI 路径只改宿主状态，
                // 读旧缓存会与实际窗口脱节——Tick 门控随之失效（控制台停止接收新日志）。
                return _host != null ? _host.ShowFullWindow : _showFullWindow;
            }
            set
            {
                _showFullWindow = value;
                if (_host != null)
                {
                    _host.ShowFullWindow = value;
                }
            }
        }

        /// <inheritdoc />
        public override DebuggerWindowRegistry WindowRegistry => _windowRegistry;

        /// <inheritdoc />
        public override DebuggerLogCapture LogCapture => _logCapture;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        protected override void OnInit()
        {
            _windowRegistry = new DebuggerWindowRegistry();
            _logCapture = new DebuggerLogCapture(m_Config != null && m_Config.ConsoleCapacity > 0 ? m_Config.ConsoleCapacity : DEFAULT_CONSOLE_CAPACITY);
            _logCapture.Start();
            _showFullWindow = false;
            _hostPending = false;

            RegisterBuiltInWindows();

            _activeWindow = ResolveActivation(DebuggerServiceSettings.ActiveWindowType);
            if (_activeWindow)
            {
                _hostPending = true;
            }
        }

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            _activeWindow = false;
            _showFullWindow = false;
            _hostPending = false;

            if (_host != null)
            {
                _host.Shutdown();
                _host = null;
            }

            _windowBuffer.Clear();
            _windowRegistry.CollectWindows(_windowBuffer);
            for (int i = 0; i < _windowBuffer.Count; i++)
            {
                _windowBuffer[i].Shutdown();
            }

            _windowBuffer.Clear();
            _logCapture.Stop();
            _windowRegistry = null;
            _logCapture = null;
        }

        /// <inheritdoc />
        public override void Tick(float elapseSeconds, float realElapseSeconds)
        {
            _logCapture.Drain();

            if (_hostPending && _activeWindow)
            {
                _hostPending = false;
                CreateHost();
            }

            // 完整窗口展开（以宿主实际状态为准——UI 开合路径不经过处理器字段）时才轮询可见窗口
            if (!_activeWindow || _host == null || !_host.ShowFullWindow)
            {
                return;
            }

            IDebuggerWindow selectedWindow = _windowRegistry.SelectedWindow;
            selectedWindow?.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        #endregion

        #region 窗口管理 [WINDOW MANAGEMENT]

        /// <inheritdoc />
        public override void RegisterDebuggerWindow(string path, IDebuggerWindow window, params object[] args)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new GameException("Path is invalid.");
            }

            if (window == null)
            {
                throw new GameException("Debugger window is invalid.");
            }

            _windowRegistry.Register(path, window);
            window.Initialize(args);
        }

        /// <inheritdoc />
        public override bool UnregisterDebuggerWindow(string path)
        {
            IDebuggerWindow window = _windowRegistry.GetWindow(path);
            if (window == null)
            {
                return false;
            }

            bool wasSelected = ReferenceEquals(_windowRegistry.SelectedWindow, window);
            if (!_windowRegistry.Unregister(path))
            {
                return false;
            }

            if (wasSelected)
            {
                _showFullWindow = false;
                if (_host != null)
                {
                    _host.ShowFullWindow = false;
                }
            }

            window.Shutdown();
            return true;
        }

        /// <inheritdoc />
        public override IDebuggerWindow GetDebuggerWindow(string path)
        {
            return _windowRegistry.GetWindow(path);
        }

        /// <inheritdoc />
        public override bool SelectDebuggerWindow(string path)
        {
            return _windowRegistry.SelectWindow(path);
        }

        #endregion

        #region 私有 [PRIVATE]

        private const int DEFAULT_CONSOLE_CAPACITY = 256;

        private void CreateHost()
        {
            if (_host != null)
            {
                return;
            }

            GameObject hostObject = new GameObject("[Debugger Runtime Host]");
            Object.DontDestroyOnLoad(hostObject);
            _host = hostObject.AddComponent<DebuggerRuntimeHost>();
            _host.Initialize(this, m_Config);
            _host.ShowFullWindow = _showFullWindow;
        }

        private static bool ResolveActivation(DebuggerActiveWindowType activeWindowType)
        {
            switch (activeWindowType)
            {
                case DebuggerActiveWindowType.AlwaysOpen:
                    return true;

                case DebuggerActiveWindowType.OnlyOpenWhenDevelopment:
                    return Debug.isDebugBuild || CommandLineUtility.GetShowDebugger();

                case DebuggerActiveWindowType.OnlyOpenInEditor:
                    return Application.isEditor || CommandLineUtility.GetShowDebugger();

                default:
                    return CommandLineUtility.GetShowDebugger();
            }
        }

        private void RegisterBuiltInWindows()
        {
            RegisterDebuggerWindow("Console", new ConsoleWindow(), _logCapture);
            RegisterDebuggerWindow("Information/System", new SystemInformationWindow());
            RegisterDebuggerWindow("Information/Environment", new EnvironmentInformationWindow());
            RegisterDebuggerWindow("Information/Screen", new ScreenInformationWindow());
            RegisterDebuggerWindow("Information/Graphics", new GraphicsInformationWindow());
            RegisterDebuggerWindow("Information/Input", new InputInformationWindow());
            RegisterDebuggerWindow("Information/Other/Scene", new SceneInformationWindow());
            RegisterDebuggerWindow("Information/Other/Time", new TimeInformationWindow());
            RegisterDebuggerWindow("Information/Other/Quality", new QualityInformationWindow());
            RegisterDebuggerWindow("Information/Other/Path", new PathInformationWindow());
            RegisterDebuggerWindow("Profiler/Summary", new ProfilerInformationWindow());
            RegisterDebuggerWindow("Profiler/Memory/Summary", new RuntimeMemorySummaryWindow());
            RegisterDebuggerWindow("Profiler/Memory/All", new RuntimeMemoryInformationWindow<Object>());
            RegisterDebuggerWindow("Profiler/Memory/Texture", new RuntimeMemoryInformationWindow<Texture>());
            RegisterDebuggerWindow("Profiler/Memory/Mesh", new RuntimeMemoryInformationWindow<Mesh>());
            RegisterDebuggerWindow("Profiler/Memory/Material", new RuntimeMemoryInformationWindow<Material>());
            RegisterDebuggerWindow("Profiler/Memory/Shader", new RuntimeMemoryInformationWindow<Shader>());
            RegisterDebuggerWindow("Profiler/Memory/AnimationClip", new RuntimeMemoryInformationWindow<AnimationClip>());
            RegisterDebuggerWindow("Profiler/Memory/AudioClip", new RuntimeMemoryInformationWindow<AudioClip>());
            RegisterDebuggerWindow("Profiler/Memory/Font", new RuntimeMemoryInformationWindow<Font>());
            RegisterDebuggerWindow("Profiler/Memory/TextAsset", new RuntimeMemoryInformationWindow<TextAsset>());
            RegisterDebuggerWindow("Profiler/Memory/ScriptableObject", new RuntimeMemoryInformationWindow<ScriptableObject>());
            RegisterDebuggerWindow("Profiler/Object Pool", new GenericObjectPoolInformationWindow());
            RegisterDebuggerWindow("Profiler/GameObject Pool", new GameObjectPoolInformationWindow());
            RegisterDebuggerWindow("Profiler/Memory Pool", new MemoryPoolInformationWindow());
            RegisterDebuggerWindow("Profiler/Service System", new ServiceSystemInformationWindow());
            RegisterDebuggerWindow("Other/Settings", new SettingsWindow());
            RegisterDebuggerWindow("Other/Game Settings", new GameAppInformationWindow());
            RegisterDebuggerWindow("Other/Operations", new OperationsWindow());
        }

        #endregion
    }
}
