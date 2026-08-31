using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器运行时宿主（MonoBehaviour）。
    /// <para>由 <see cref="DefaultDebuggerHandler"/> 在首个 Tick 懒建（DontDestroyOnLoad）；运行时构建 <c>PanelSettings</c> / <c>UIDocument</c>（零资产依赖，任何构建管线无需收集调试器资产）。</para>
    /// <para>承载：悬浮 FPS 入口（拖拽 + 边缘吸附 + 日志级别着色）、主窗口 chrome（侧边栏树 + 搜索过滤 + 内容区 + 拖动/缩放）、布局持久化与参考分辨率自适应缩放。</para>
    /// <para>面板缩放策略：面板尺寸 = 屏幕尺寸 / (分辨率比例 × 用户缩放)——全部控件按 1920×1080 参考坐标编写，不感知缩放。</para>
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class DebuggerRuntimeHost : MonoBehaviour
    {
        #region 常量 [CONSTANTS]

        private const float REFERENCE_WIDTH = 1920f;
        private const float REFERENCE_HEIGHT = 1080f;
        private const float MIN_RESOLUTION_SCALE = 0.5f;
        private const float MAX_RESOLUTION_SCALE = 3f;
        private const float MIN_WINDOW_SCALE = 0.5f;
        private const float MAX_WINDOW_SCALE = 2f;
        private const float MIN_WINDOW_WIDTH = 640f;
        private const float MIN_WINDOW_HEIGHT = 400f;
        private const float PANEL_SORTING_ORDER = 16000f;
        private const string PANEL_SETTINGS_RESOURCE_PATH = "DebuggerPanelSettings";
        private const string THEME_RESOURCE_PATH = "Debugger UI Theme";
        private const float TOGGLE_CLICK_MOVE_THRESHOLD = 8f;
        private const float TOGGLE_DOUBLE_CLICK_INTERVAL = 0.35f;
        private const float TOGGLE_DOUBLE_CLICK_MAX_DISTANCE = 24f;
        private const float TOGGLE_SNAP_SMOOTH_TIME = 0.08f;
        private const float TOGGLE_SNAP_STOP_DISTANCE = 0.5f;
        private const string ROOT_WINDOW_TITLE = "DEBUGGER";
        private const string FPS_NUMBER_CLASS_WARNING = "dbg-fps-btn__number--warning";
        private const string FPS_NUMBER_CLASS_DANGER = "dbg-fps-btn__number--danger";
        private const string FPS_NUMBER_CLASS_FATAL = "dbg-fps-btn__number--fatal";

        private static readonly Rect DEFAULT_ICON_RECT = new Rect(16f, 16f, 132f, 70f);
        private static readonly Rect DEFAULT_WINDOW_RECT = new Rect(16f, 84f, 1280f, 880f);

        #endregion

        #region 字段 [FIELDS]

        /// <summary>
        /// 获取运行时宿主单例（未创建为 null）。
        /// </summary>
        public static DebuggerRuntimeHost Instance
        {
            get;
            private set;
        }

        private DefaultDebuggerHandler _handler;
        private DebuggerWindowRegistry _registry;
        private FpsCounter _fpsCounter;
        private DebuggerStatsOverlay _statsOverlay;

        private PanelSettings _panelSettings;
        private Font _runtimeSystemFont;
        private UIDocument _uiDocument;
        private VisualElement _root;
        private VisualElement _toggleButton;
        private Label _toggleFpsLabel;
        private VisualElement _overlay;
        private VisualElement _window;
        private Label _headerTitle;
        private ScrollView _sidebarScroll;
        private TextField _searchField;
        private VisualElement _contentHost;
        private readonly Dictionary<IDebuggerWindow, VisualElement> _viewByWindow = new Dictionary<IDebuggerWindow, VisualElement>(32);

        private Rect _iconRect = DEFAULT_ICON_RECT;
        private Rect _windowRect = DEFAULT_WINDOW_RECT;
        private float _windowScale = 1f;
        private bool _showFullWindow;
        private bool _rootVisible = true;
        private int _lastRegistryVersion = -1;
        private float _lastFpsLabelValue = -1f;
        private int _lastLogVersion = -1;

        private Vector2 _dragPointerStart;
        private Vector2 _dragWindowStart;
        private bool _isToggleDragging;
        private bool _isToggleSnapAnimating;
        private Vector2 _toggleSnapVelocity;
        private Vector2 _toggleSnapTarget;
        private float _lastToggleTapTime = -1f;
        private Vector2 _lastToggleTapPosition;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取或设置完整调试器窗口是否展开（选中窗口的 OnEnter/OnLeave 随之配对）。
        /// </summary>
        public bool ShowFullWindow
        {
            get
            {
                return _showFullWindow;
            }
            set
            {
                if (_showFullWindow == value)
                {
                    return;
                }

                _showFullWindow = value;
                if (_showFullWindow && _registry.SelectedWindow == null)
                {
                    SelectFirstWindow();
                }

                SyncWindowVisibility();

                if (_showFullWindow)
                {
                    _registry.SelectedWindow?.OnEnter();
                }
                else
                {
                    _registry.SelectedWindow?.OnLeave();
                }

                UpdateHeaderTitle();
                RefreshSelectionContent();
                RebuildSidebar();
            }
        }

        /// <summary>
        /// 获取或设置常驻统计 HUD 可见性。
        /// </summary>
        public bool StatsOverlayVisible
        {
            get
            {
                return _statsOverlay != null && _statsOverlay.Visible;
            }
            set
            {
                EnsureStatsOverlay();
                _statsOverlay.Visible = value;
            }
        }

        /// <summary>
        /// 获取或设置用户窗口缩放（0.5-2，重建面板缩放）。
        /// </summary>
        public float WindowScale
        {
            get
            {
                return _windowScale;
            }
            set
            {
                float clamped = Mathf.Clamp(value, MIN_WINDOW_SCALE, MAX_WINDOW_SCALE);
                if (Mathf.Approximately(_windowScale, clamped))
                {
                    return;
                }

                _windowScale = clamped;
                ApplyPanelScale();
                SettingUtility.SetFloat(Constant.Debug.WINDOW_SCALE, _windowScale);
                SettingUtility.Save();
            }
        }

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 由默认处理器接线（创建即构建全部运行时 UI）。
        /// </summary>
        /// <param name="handler">宿主所属处理器。</param>
        /// <param name="config">后端配置。</param>
        internal void Initialize(DefaultDebuggerHandler handler, DefaultDebuggerHandlerConfig config)
        {
            _handler = handler;
            _registry = handler.WindowRegistry;
            _fpsCounter = new FpsCounter(config != null && config.FpsUpdateInterval > 0f ? config.FpsUpdateInterval : 0.5f);

            LoadLayoutSettings();
            EnsureRuntimePanel();

            if (config != null && config.StatsOverlayVisible)
            {
                StatsOverlayVisible = true;
            }

            gameObject.name = "[Debugger Runtime Host]";
            gameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// 设置根元素整体可见性（调试器激活开关）。
        /// </summary>
        /// <param name="visible">是否可见。</param>
        internal void SetRootVisible(bool visible)
        {
            _rootVisible = visible;
            if (_root != null)
            {
                _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>
        /// 处理器关停前显式释放（销毁宿主对象）。
        /// </summary>
        internal void Shutdown()
        {
            if (_showFullWindow)
            {
                _registry?.SelectedWindow?.OnLeave();
                _showFullWindow = false;
            }

            _statsOverlay?.Dispose();
            _statsOverlay = null;
            _viewByWindow.Clear();
            _handler = null;
            _registry = null;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            SettingUtility.Save();

            if (Instance == this)
            {
                Instance = null;
            }

            if (_uiDocument != null)
            {
                Destroy(_uiDocument);
                _uiDocument = null;
            }

            if (_panelSettings != null)
            {
                Destroy(_panelSettings);
                _panelSettings = null;
            }

            if (_runtimeSystemFont != null)
            {
                Destroy(_runtimeSystemFont);
                _runtimeSystemFont = null;
            }
        }

        #endregion

        #region 轮询 [UPDATE]

        private void Update()
        {
            if (_registry == null)
            {
                return;
            }

            _fpsCounter.Update(Time.unscaledDeltaTime);
            _statsOverlay?.Tick(Time.unscaledDeltaTime);
            UpdateToggleState();
            UpdateToggleSnapAnimation(Time.unscaledDeltaTime);

            if (_lastRegistryVersion != _registry.Version)
            {
                _lastRegistryVersion = _registry.Version;
                RebuildSidebar();
                RefreshSelectionContent();
                SyncWindowVisibility();
            }
        }

        #endregion

        #region 运行时面板 [RUNTIME PANEL]

        private void EnsureRuntimePanel()
        {
            // 优先克隆包内 PanelSettings 资产（内嵌共享主题 "Debugger UI Theme.tss" 引用——
            // CreateInstance 在 Play 模式下 themeStyleSheet 为 null，全部内置控件失去基础 USS，布局完全错位）；
            // 资产缺失时回退 CreateInstance 并显式加载共享主题（降级渲染）。
            PanelSettings source = Resources.Load<PanelSettings>(PANEL_SETTINGS_RESOURCE_PATH);
            if (source != null)
            {
                _panelSettings = Instantiate(source);
            }
            else
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>(THEME_RESOURCE_PATH);
            }

            _panelSettings.name = "Debugger Runtime Panel Settings";
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.referenceDpi = 96f;
            _panelSettings.fallbackDpi = 96f;
            _panelSettings.clearColor = false;
            _panelSettings.match = 0f;
            // textSettings 不覆写（保持空——根节点经 ResolveFontDefinition 显式设置 OS 字体定义，
            // 空 PanelTextSettings 反而截断主题默认字体回退链）。
            ApplyPanelScale();

            _uiDocument = gameObject.AddComponent<UIDocument>();
            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.sortingOrder = PANEL_SORTING_ORDER;

            BuildRootVisualTree();
        }

        private void ApplyPanelScale()
        {
            if (_panelSettings == null)
            {
                return;
            }

            _panelSettings.scale = GetResolutionScale() * _windowScale;
        }

        private static float GetResolutionScale()
        {
            float widthScale = Screen.width > 0 ? Screen.width / REFERENCE_WIDTH : 1f;
            float heightScale = Screen.height > 0 ? Screen.height / REFERENCE_HEIGHT : 1f;
            return Mathf.Clamp(Mathf.Min(widthScale, heightScale), MIN_RESOLUTION_SCALE, MAX_RESOLUTION_SCALE);
        }

        private void BuildRootVisualTree()
        {
            _root = _uiDocument.rootVisualElement;
            _root.Clear();
            _root.name = "moirai-debugger-root";
            _root.AddToClassList("dbg-root");
            _root.pickingMode = PickingMode.Ignore;
            _root.style.display = _rootVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _root.style.unityFontDefinition = ResolveFontDefinition();

            _toggleButton = BuildToggleButton();
            _overlay = BuildWindowOverlay();

            _root.Add(_toggleButton);
            _root.Add(_overlay);

            _root.RegisterCallback<GeometryChangedEvent>(_ => ClampLayoutToScreen());

            _lastRegistryVersion = -1;
            ApplyIconRect();
            ApplyWindowRect();
            UpdateHeaderTitle();
            RebuildSidebar();
            RefreshSelectionContent();
            SyncWindowVisibility();
        }

        #endregion

        #region 字体 [FONT RESOLUTION]

        private StyleFontDefinition ResolveFontDefinition()
        {
            Font runtimeFont = ResolveRuntimeFont();
            if (runtimeFont != null)
            {
                return new StyleFontDefinition(FontDefinition.FromFont(runtimeFont));
            }

            return new StyleFontDefinition(StyleKeyword.Null);
        }

        private Font ResolveRuntimeFont()
        {
            if (_runtimeSystemFont != null)
            {
                return _runtimeSystemFont;
            }

            _runtimeSystemFont = CreateSystemFallbackFont();
            return _runtimeSystemFont;
        }

        private static Font CreateSystemFallbackFont()
        {
            string[] candidates =
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "Segoe UI",
                "Arial",
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS
                "PingFang SC",
                "Hiragino Sans GB",
                "Helvetica Neue",
                "Arial",
#elif UNITY_ANDROID
                "Noto Sans CJK SC",
                "Roboto",
                "Droid Sans Fallback",
#elif UNITY_WEBGL
                "Arial",
#else
                "Arial",
                "Helvetica",
                "Roboto",
#endif
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    Font font = Font.CreateDynamicFontFromOSFont(candidates[i], 14);
                    if (font != null)
                    {
                        font.hideFlags = HideFlags.HideAndDontSave;
                        return font;
                    }
                }
                catch
                {
                    // 候选字体缺失时继续尝试下一候选。
                }
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        #endregion

        #region 悬浮入口 [FLOATING TOGGLE]

        private VisualElement BuildToggleButton()
        {
            VisualElement button = new VisualElement();
            button.name = "debugger-toggle";
            button.tooltip = "Drag to move — double-click to open the debugger";
            button.AddToClassList("dbg-fps-btn");
            button.pickingMode = PickingMode.Position;
            button.usageHints = UsageHints.DynamicTransform;

            Label fpsCaption = new Label("FPS");
            fpsCaption.AddToClassList("dbg-fps-btn__caption");
            fpsCaption.pickingMode = PickingMode.Ignore;

            _toggleFpsLabel = new Label("--");
            _toggleFpsLabel.AddToClassList("dbg-fps-btn__number");
            _toggleFpsLabel.pickingMode = PickingMode.Ignore;

            button.Add(fpsCaption);
            button.Add(_toggleFpsLabel);
            RegisterToggleManipulator(button);
            return button;
        }

        private void RegisterToggleManipulator(VisualElement target)
        {
            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                target.CapturePointer(evt.pointerId);
                _isToggleDragging = false;
                _isToggleSnapAnimating = false;
                _toggleSnapVelocity = Vector2.zero;
                _dragPointerStart = new Vector2(evt.position.x, evt.position.y);
                _dragWindowStart = new Vector2(_iconRect.x, _iconRect.y);
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 currentPosition = new Vector2(evt.position.x, evt.position.y);
                Vector2 delta = currentPosition - _dragPointerStart;
                if (!_isToggleDragging && delta.sqrMagnitude > TOGGLE_CLICK_MOVE_THRESHOLD * TOGGLE_CLICK_MOVE_THRESHOLD)
                {
                    _isToggleDragging = true;
                }

                if (_isToggleDragging)
                {
                    Vector2 targetPosition = ClampIconPosition(_dragWindowStart + delta);
                    _iconRect = new Rect(targetPosition.x, targetPosition.y, _iconRect.width, _iconRect.height);
                    ApplyIconRect();
                }

                evt.StopPropagation();
            });

            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                target.ReleasePointer(evt.pointerId);
                if (_isToggleDragging)
                {
                    SaveIconLayoutSettings();
                    StartToggleSnapAnimation();
                }
                else if (HandleToggleTap(Time.unscaledTime, new Vector2(evt.position.x, evt.position.y)))
                {
                    ShowFullWindow = true;
                }

                _isToggleDragging = false;
                evt.StopPropagation();
            });
        }

        /// <summary>
        /// 悬浮入口点击判定——双击（间隔与位移阈值内）展开面板，单击仅记录等待第二次点击。
        /// </summary>
        /// <param name="unscaledTime">当前未缩放时间。</param>
        /// <param name="position">本次点击位置（面板参考坐标）。</param>
        /// <returns>是否构成双击。</returns>
        internal bool HandleToggleTap(float unscaledTime, Vector2 position)
        {
            bool isDoubleClick =
                _lastToggleTapTime >= 0f &&
                unscaledTime - _lastToggleTapTime <= TOGGLE_DOUBLE_CLICK_INTERVAL &&
                (position - _lastToggleTapPosition).sqrMagnitude <= TOGGLE_DOUBLE_CLICK_MAX_DISTANCE * TOGGLE_DOUBLE_CLICK_MAX_DISTANCE;

            if (isDoubleClick)
            {
                _lastToggleTapTime = -1f;
                return true;
            }

            _lastToggleTapTime = unscaledTime;
            _lastToggleTapPosition = position;
            return false;
        }

        private void UpdateToggleState()
        {
            if (_toggleFpsLabel == null || _fpsCounter == null || _handler == null)
            {
                return;
            }

            float currentFps = _fpsCounter.CurrentFps;
            if (!Mathf.Approximately(_lastFpsLabelValue, currentFps))
            {
                _lastFpsLabelValue = currentFps;
                _toggleFpsLabel.text = StringUtility.Format("{0:F0}", currentFps);
            }

            DebuggerLogCapture logCapture = _handler.LogCapture;
            if (logCapture != null && _lastLogVersion != logCapture.Version)
            {
                _lastLogVersion = logCapture.Version;
                ApplyWorstLogClass(GetWorstLogClass(logCapture));
            }
        }

        private static string GetWorstLogClass(DebuggerLogCapture logCapture)
        {
            if (logCapture.FatalCount > 0)
            {
                return FPS_NUMBER_CLASS_FATAL;
            }

            if (logCapture.ErrorCount > 0)
            {
                return FPS_NUMBER_CLASS_DANGER;
            }

            if (logCapture.WarningCount > 0)
            {
                return FPS_NUMBER_CLASS_WARNING;
            }

            return null;
        }

        private void ApplyWorstLogClass(string className)
        {
            if (_toggleFpsLabel == null)
            {
                return;
            }

            _toggleFpsLabel.RemoveFromClassList(FPS_NUMBER_CLASS_WARNING);
            _toggleFpsLabel.RemoveFromClassList(FPS_NUMBER_CLASS_DANGER);
            _toggleFpsLabel.RemoveFromClassList(FPS_NUMBER_CLASS_FATAL);
            if (className != null)
            {
                _toggleFpsLabel.AddToClassList(className);
            }
        }

        private void UpdateToggleSnapAnimation(float unscaledDeltaTime)
        {
            if (!_isToggleSnapAnimating)
            {
                return;
            }

            Vector2 current = new Vector2(_iconRect.x, _iconRect.y);
            Vector2 next = Vector2.SmoothDamp(current, _toggleSnapTarget, ref _toggleSnapVelocity, TOGGLE_SNAP_SMOOTH_TIME, Mathf.Infinity, unscaledDeltaTime);
            _iconRect = new Rect(next.x, next.y, _iconRect.width, _iconRect.height);
            ApplyIconRect();

            if ((next - _toggleSnapTarget).sqrMagnitude <= TOGGLE_SNAP_STOP_DISTANCE * TOGGLE_SNAP_STOP_DISTANCE)
            {
                _isToggleSnapAnimating = false;
                _toggleSnapVelocity = Vector2.zero;
                _iconRect = new Rect(_toggleSnapTarget.x, _toggleSnapTarget.y, _iconRect.width, _iconRect.height);
                ApplyIconRect();
                SaveIconLayoutSettings();
            }
        }

        private void StartToggleSnapAnimation()
        {
            _toggleSnapTarget = GetSnappedIconPosition(new Vector2(_iconRect.x, _iconRect.y));
            _isToggleSnapAnimating = true;
            _toggleSnapVelocity = Vector2.zero;
        }

        private Vector2 GetSnappedIconPosition(Vector2 position)
        {
            Rect screen = GetScreenRect();
            float centerX = position.x + _iconRect.width * 0.5f;
            return centerX < screen.center.x
                ? new Vector2(screen.xMin, position.y)
                : new Vector2(screen.xMax - _iconRect.width, position.y);
        }

        private Vector2 ClampIconPosition(Vector2 position)
        {
            Rect screen = GetScreenRect();
            float x = Mathf.Clamp(position.x, screen.xMin, screen.xMax - _iconRect.width);
            float y = Mathf.Clamp(position.y, screen.yMin, screen.yMax - _iconRect.height);
            return new Vector2(x, y);
        }

        private void ApplyIconRect()
        {
            if (_toggleButton == null)
            {
                return;
            }

            _toggleButton.style.left = _iconRect.x;
            _toggleButton.style.top = _iconRect.y;
            _toggleButton.style.width = _iconRect.width;
            _toggleButton.style.height = _iconRect.height;
        }

        /// <summary>
        /// 获取或设置悬浮入口矩形（面板参考坐标）。
        /// </summary>
        internal Rect IconRect
        {
            get
            {
                return _iconRect;
            }
            set
            {
                Vector2 clamped = ClampIconPosition(new Vector2(value.x, value.y));
                _iconRect = new Rect(clamped.x, clamped.y, _iconRect.width, _iconRect.height);
                ApplyIconRect();
            }
        }

        #endregion

        #region 主窗口 [MAIN WINDOW]

        private VisualElement BuildWindowOverlay()
        {
            VisualElement overlay = new VisualElement();
            overlay.name = "debugger-overlay";
            overlay.AddToClassList("dbg-overlay");
            overlay.pickingMode = PickingMode.Ignore;

            _window = new VisualElement();
            _window.name = "debugger-window";
            _window.AddToClassList("dbg-window");
            _window.style.width = _windowRect.width;
            _window.style.height = _windowRect.height;
            _window.pickingMode = PickingMode.Position;
            _window.usageHints = UsageHints.DynamicTransform;

            _window.Add(BuildWindowHeader());
            _window.Add(BuildWindowBody());
            _window.Add(BuildResizeHandle());

            overlay.Add(_window);
            return overlay;
        }

        private VisualElement BuildWindowHeader()
        {
            VisualElement header = new VisualElement();
            header.name = "debugger-window-header";
            header.AddToClassList("dbg-window__header");

            VisualElement headerLeft = new VisualElement();
            headerLeft.style.flexGrow = 1f;
            headerLeft.style.minWidth = 0f;

            _headerTitle = new Label(ROOT_WINDOW_TITLE);
            _headerTitle.AddToClassList("dbg-window__header-title");
            headerLeft.Add(_headerTitle);

            VisualElement headerActions = new VisualElement();
            headerActions.AddToClassList("dbg-window__header-actions");

            Button resetButton = CreateHeaderButton("Reset", "Reset Layout", false, ResetLayout);
            resetButton.style.marginRight = 8f;
            Button closeButton = CreateHeaderButton("×", "Close", true, () => ShowFullWindow = false);

            headerActions.Add(resetButton);
            headerActions.Add(closeButton);

            header.Add(headerLeft);
            header.Add(headerActions);

            RegisterWindowDragManipulator(header);
            return header;
        }

        private static Button CreateHeaderButton(string text, string tooltip, bool isClose, System.Action onClick)
        {
            Button button = new Button(onClick)
            {
                text = text
            };
            button.tooltip = tooltip;
            button.AddToClassList("dbg-header-btn");
            if (isClose)
            {
                button.AddToClassList("dbg-header-btn--close");
            }

            return button;
        }

        private VisualElement BuildWindowBody()
        {
            VisualElement body = new VisualElement();
            body.name = "debugger-window-body";
            body.AddToClassList("dbg-window__body");

            VisualElement sidebar = new VisualElement();
            sidebar.name = "debugger-sidebar";
            sidebar.AddToClassList("dbg-sidebar");

            _searchField = DebuggerUI.CreateSearchField("Search windows...", _ => RebuildSidebar());
            _searchField.name = "debugger-sidebar-search";
            _searchField.AddToClassList("dbg-sidebar__search");
            VisualElement searchWrapper = (VisualElement)_searchField.userData;
            sidebar.Add(searchWrapper);

            _sidebarScroll = new ScrollView(ScrollViewMode.Vertical);
            _sidebarScroll.AddToClassList("dbg-sidebar__scroll");
            _sidebarScroll.style.flexGrow = 1f;
            _sidebarScroll.style.minHeight = 0f;
            _sidebarScroll.contentContainer.style.flexDirection = FlexDirection.Column;
            _sidebarScroll.mouseWheelScrollSize = 240f;
            sidebar.Add(_sidebarScroll);

            _contentHost = new VisualElement();
            _contentHost.name = "debugger-content-host";
            _contentHost.AddToClassList("dbg-window__content");
            _contentHost.pickingMode = PickingMode.Position;

            body.Add(sidebar);
            body.Add(_contentHost);
            return body;
        }

        private VisualElement BuildResizeHandle()
        {
            VisualElement resizeHandle = new VisualElement();
            resizeHandle.name = "debugger-resize-handle";
            resizeHandle.AddToClassList("dbg-resize-handle");
            resizeHandle.pickingMode = PickingMode.Position;

            Label resizeGlyph = new Label("//");
            resizeGlyph.AddToClassList("dbg-resize-handle__glyph");
            resizeGlyph.pickingMode = PickingMode.Ignore;
            resizeHandle.Add(resizeGlyph);

            RegisterResizeManipulator(resizeHandle);
            return resizeHandle;
        }

        private void RegisterWindowDragManipulator(VisualElement target)
        {
            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                target.CapturePointer(evt.pointerId);
                _dragPointerStart = new Vector2(evt.position.x, evt.position.y);
                _dragWindowStart = new Vector2(_windowRect.x, _windowRect.y);
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 delta = new Vector2(evt.position.x, evt.position.y) - _dragPointerStart;
                WindowRect = new Rect(_dragWindowStart.x + delta.x, _dragWindowStart.y + delta.y, _windowRect.width, _windowRect.height);
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                target.ReleasePointer(evt.pointerId);
                SaveWindowLayoutSettings();
                evt.StopPropagation();
            });
        }

        private void RegisterResizeManipulator(VisualElement target)
        {
            target.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                target.CapturePointer(evt.pointerId);
                _dragPointerStart = new Vector2(evt.position.x, evt.position.y);
                _dragWindowStart = new Vector2(_windowRect.width, _windowRect.height);
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                Vector2 delta = new Vector2(evt.position.x, evt.position.y) - _dragPointerStart;
                WindowRect = new Rect(_windowRect.x, _windowRect.y, Mathf.Max(MIN_WINDOW_WIDTH, _dragWindowStart.x + delta.x), Mathf.Max(MIN_WINDOW_HEIGHT, _dragWindowStart.y + delta.y));
                evt.StopPropagation();
            });

            target.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!target.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                target.ReleasePointer(evt.pointerId);
                SaveWindowLayoutSettings();
                evt.StopPropagation();
            });
        }

        /// <summary>
        /// 获取或设置主窗口矩形（面板参考坐标，自动钳制在屏幕内）。
        /// </summary>
        internal Rect WindowRect
        {
            get
            {
                return _windowRect;
            }
            set
            {
                Rect screen = GetScreenRect();
                float x = Mathf.Clamp(value.x, screen.xMin, screen.xMax - MIN_WINDOW_WIDTH);
                float y = Mathf.Clamp(value.y, screen.yMin, screen.yMax - MIN_WINDOW_HEIGHT);
                _windowRect = new Rect(x, y, Mathf.Max(MIN_WINDOW_WIDTH, value.width), Mathf.Max(MIN_WINDOW_HEIGHT, value.height));
                ApplyWindowRect();
            }
        }

        private void ApplyWindowRect()
        {
            if (_window == null)
            {
                return;
            }

            _window.style.left = _windowRect.x;
            _window.style.top = _windowRect.y;
            _window.style.width = _windowRect.width;
            _window.style.height = _windowRect.height;
        }

        private Rect GetScreenRect()
        {
            if (_root == null || _root.panel == null)
            {
                return new Rect(0f, 0f, REFERENCE_WIDTH, REFERENCE_HEIGHT);
            }

            Rect bounds = _root.worldBound;
            if (bounds.width < 1f || bounds.height < 1f)
            {
                return new Rect(0f, 0f, REFERENCE_WIDTH, REFERENCE_HEIGHT);
            }

            // worldBound 为屏幕像素，而 _iconRect/_windowRect 为参考坐标（面板被 scale 缩放）——
            // 除以面板缩放换算到参考坐标系，钳制/吸附才不越界。
            float scale = _panelSettings != null && _panelSettings.scale > 0f ? _panelSettings.scale : 1f;
            return new Rect(
                bounds.x / scale,
                bounds.y / scale,
                bounds.width / scale,
                bounds.height / scale);
        }

        private void ClampLayoutToScreen()
        {
            IconRect = _iconRect;
            WindowRect = _windowRect;
        }

        private void UpdateHeaderTitle()
        {
            if (_headerTitle == null || _registry == null)
            {
                return;
            }

            _headerTitle.text = _registry.SelectedNode != null ? _registry.SelectedNode.Path : ROOT_WINDOW_TITLE;
        }

        private void SyncWindowVisibility()
        {
            if (_toggleButton != null)
            {
                _toggleButton.style.display = _rootVisible && !_showFullWindow ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_overlay != null)
            {
                _overlay.style.display = _rootVisible && _showFullWindow ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        #endregion

        #region 侧边栏 [SIDEBAR]

        private void RebuildSidebar()
        {
            if (_sidebarScroll == null || _registry == null)
            {
                return;
            }

            VisualElement container = _sidebarScroll.contentContainer;
            container.Clear();

            string searchText = _searchField != null ? _searchField.text : string.Empty;
            if (!string.IsNullOrEmpty(searchText))
            {
                BuildSearchResults(container, searchText);
                return;
            }

            IReadOnlyList<DebuggerWindowNode> children = _registry.Root.Children;
            for (int i = 0; i < children.Count; i++)
            {
                BuildSidebarNode(container, children[i], 0);
            }
        }

        private void BuildSearchResults(VisualElement container, string searchText)
        {
            List<DebuggerWindowNode> matches = new List<DebuggerWindowNode>(32);
            CollectSearchMatches(_registry.Root, searchText, matches);

            if (matches.Count == 0)
            {
                container.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("No windows match \"{0}\".", searchText)));
                return;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                BuildSidebarRow(container, matches[i], matches[i].Path, 0);
            }
        }

        private static void CollectSearchMatches(DebuggerWindowNode node, string searchText, List<DebuggerWindowNode> results)
        {
            if (node.Window != null && node.Path.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                results.Add(node);
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                CollectSearchMatches(node.Children[i], searchText, results);
            }
        }

        private void BuildSidebarNode(VisualElement parent, DebuggerWindowNode node, int depth)
        {
            // 组节点使用内置 Foldout——自带旋转箭头与内容折叠（收起/展开即 Foldout 语义）
            if (node.IsGroup)
            {
                Foldout foldout = new Foldout
                {
                    text = node.Name,
                    value = node.Expanded
                };
                foldout.AddToClassList("dbg-sidebar-foldout");
                if (depth == 0 && parent.childCount > 0)
                {
                    foldout.AddToClassList("dbg-sidebar-gap");
                }

                // 展开状态回写节点（供选中重建时恢复），内容可见性由 Foldout 自管
                foldout.RegisterValueChangedCallback(evt => node.Expanded = evt.newValue);

                parent.Add(foldout);
                for (int i = 0; i < node.Children.Count; i++)
                {
                    BuildSidebarNode(foldout, node.Children[i], depth + 1);
                }

                return;
            }

            BuildSidebarRow(parent, node, node.Name, depth);
        }

        private void BuildSidebarRow(VisualElement parent, DebuggerWindowNode node, string displayText, int depth)
        {
            bool isSelected = ReferenceEquals(_registry.SelectedNode, node);
            VisualElement row = new VisualElement();
            row.name = "debugger-sidebar-row";
            row.AddToClassList("dbg-sidebar-row");
            if (isSelected)
            {
                row.AddToClassList("dbg-sidebar-row--selected");
            }

            if (depth == 0 && parent.childCount > 0)
            {
                row.AddToClassList("dbg-sidebar-gap");
            }

            row.pickingMode = PickingMode.Position;

            Label titleLabel = new Label(displayText);
            titleLabel.AddToClassList("dbg-sidebar-row__title");
            titleLabel.pickingMode = PickingMode.Ignore;
            row.Add(titleLabel);

            row.AddManipulator(new Clickable(() => SelectWindowNode(node)));

            parent.Add(row);
        }

        private void SelectWindowNode(DebuggerWindowNode node)
        {
            if (!ReferenceEquals(_registry.SelectedNode, node))
            {
                if (_showFullWindow)
                {
                    _registry.SelectedWindow?.OnLeave();
                }

                _registry.SelectNode(node);

                if (_showFullWindow)
                {
                    node.Window.OnEnter();
                }
            }

            if (!_showFullWindow)
            {
                ShowFullWindow = true;
                return;
            }

            UpdateHeaderTitle();
            RefreshSelectionContent();
            RebuildSidebar();
        }

        private void SelectFirstWindow()
        {
            IReadOnlyList<DebuggerWindowNode> children = _registry.Root.Children;
            if (children.Count <= 0)
            {
                return;
            }

            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].Window != null)
                {
                    _registry.SelectNode(children[i]);
                    return;
                }
            }
        }

        private void RefreshSelectionContent()
        {
            UpdateHeaderTitle();

            if (_contentHost == null)
            {
                return;
            }

            _contentHost.Clear();
            IDebuggerWindow selectedWindow = _registry.SelectedWindow;
            if (selectedWindow == null)
            {
                _contentHost.Add(DebuggerUI.CreateHintLabel("无已注册窗口——经 DebuggerService.RegisterDebuggerWindow 注册。"));
                return;
            }

            if (!_viewByWindow.TryGetValue(selectedWindow, out VisualElement view) || view == null)
            {
                view = selectedWindow.CreateView();
                _viewByWindow[selectedWindow] = view;
            }

            if (view != null)
            {
                _contentHost.Add(view);
            }
        }

        #endregion

        #region 布局持久化 [LAYOUT PERSISTENCE]

        private void LoadLayoutSettings()
        {
            float iconX = SettingUtility.GetFloat(Constant.Debug.ICON_X, DEFAULT_ICON_RECT.x);
            float iconY = SettingUtility.GetFloat(Constant.Debug.ICON_Y, DEFAULT_ICON_RECT.y);
            float windowX = SettingUtility.GetFloat(Constant.Debug.WINDOW_X, DEFAULT_WINDOW_RECT.x);
            float windowY = SettingUtility.GetFloat(Constant.Debug.WINDOW_Y, DEFAULT_WINDOW_RECT.y);
            float windowWidth = SettingUtility.GetFloat(Constant.Debug.WINDOW_WIDTH, DEFAULT_WINDOW_RECT.width);
            float windowHeight = SettingUtility.GetFloat(Constant.Debug.WINDOW_HEIGHT, DEFAULT_WINDOW_RECT.height);

            _windowScale = Mathf.Clamp(SettingUtility.GetFloat(Constant.Debug.WINDOW_SCALE, 1f), MIN_WINDOW_SCALE, MAX_WINDOW_SCALE);
            _iconRect = new Rect(iconX, iconY, DEFAULT_ICON_RECT.width, DEFAULT_ICON_RECT.height);
            _windowRect = new Rect(windowX, windowY, windowWidth, windowHeight);
        }

        private void SaveIconLayoutSettings()
        {
            SettingUtility.SetFloat(Constant.Debug.ICON_X, _iconRect.x);
            SettingUtility.SetFloat(Constant.Debug.ICON_Y, _iconRect.y);
        }

        private void SaveWindowLayoutSettings()
        {
            SettingUtility.SetFloat(Constant.Debug.WINDOW_X, _windowRect.x);
            SettingUtility.SetFloat(Constant.Debug.WINDOW_Y, _windowRect.y);
            SettingUtility.SetFloat(Constant.Debug.WINDOW_WIDTH, _windowRect.width);
            SettingUtility.SetFloat(Constant.Debug.WINDOW_HEIGHT, _windowRect.height);
        }

        internal void ResetLayout()
        {
            _iconRect = DEFAULT_ICON_RECT;
            _windowRect = DEFAULT_WINDOW_RECT;
            _windowScale = 1f;
            ApplyPanelScale();
            ApplyIconRect();
            ApplyWindowRect();
            SaveIconLayoutSettings();
            SaveWindowLayoutSettings();
            SettingUtility.SetFloat(Constant.Debug.WINDOW_SCALE, _windowScale);
            SettingUtility.Save();
        }

        private void EnsureStatsOverlay()
        {
            if (_statsOverlay != null || _root == null)
            {
                return;
            }

            _statsOverlay = new DebuggerStatsOverlay(_fpsCounter);
            _statsOverlay.Attach(_root);
        }

        #endregion
    }
}
