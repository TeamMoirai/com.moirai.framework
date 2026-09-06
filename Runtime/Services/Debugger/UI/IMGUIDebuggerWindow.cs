using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// IMGUI 调试视图适配器（将 <see cref="ServiceDebugView"/> 的 GUILayout 内容嵌入 UI Toolkit 调试器）。
    /// <para>经 <see cref="IMGUIContainer"/> 承载绘制——既有 IMGUI 调试视图零改动即可在游戏内调试器渲染；仅该窗口打开期间产生 IMGUI 同步布局开销。</para>
    /// </summary>
    public sealed class IMGUIDebuggerWindow : IDebuggerWindow
    {
        #region 字段 [FIELDS]

        private readonly ServiceDebugView _view;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 IMGUI 调试视图适配器的新实例。
        /// </summary>
        /// <param name="view">要适配的服务调试视图。</param>
        public IMGUIDebuggerWindow(ServiceDebugView view)
        {
            _view = view;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取被适配的服务调试视图。
        /// </summary>
        public ServiceDebugView View => _view;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public void Initialize(params object[] args)
        {
            _view?.Initialize(args);
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            _view?.Shutdown();
        }

        /// <inheritdoc />
        public void OnEnter()
        {
            _view?.OnEnter();
        }

        /// <inheritdoc />
        public void OnLeave()
        {
            _view?.OnLeave();
        }

        /// <inheritdoc />
        public void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            _view?.OnUpdate(elapseSeconds, realElapseSeconds);
        }

        /// <inheritdoc />
        public VisualElement CreateView()
        {
            VisualElement root = new VisualElement();
            root.style.flexGrow = 1f;

            if (_view == null)
            {
                root.Add(DebuggerUI.CreateHintLabel("Service debug view is invalid."));
                return root;
            }

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            scrollView.style.paddingLeft = 14f;
            scrollView.style.paddingRight = 14f;
            scrollView.style.paddingTop = 14f;
            scrollView.style.paddingBottom = 14f;
            DebuggerUI.StyleScrollView(scrollView);

            IMGUIContainer imguiContainer = new IMGUIContainer
            {
                onGUIHandler = DrawViewWithLiftedText
            };
            imguiContainer.style.flexGrow = 1f;
            scrollView.Add(imguiContainer);
            root.Add(scrollView);
            return root;
        }

        #endregion

        #region 私有 [PRIVATE]

        /// <summary>
        /// 绘制视图并临时提亮 IMGUI 皮肤文字色——运行时默认皮肤为黑字，在调试器深色面板上不可读。
        /// </summary>
        private void DrawViewWithLiftedText()
        {
            GUISkin skin = GUI.skin;
            Color labelColor = skin.label.normal.textColor;
            Color buttonColor = skin.button.normal.textColor;
            Color toggleColor = skin.toggle.normal.textColor;
            Color boxColor = skin.box.normal.textColor;
            skin.label.normal.textColor = Color.white;
            skin.button.normal.textColor = Color.white;
            skin.toggle.normal.textColor = Color.white;
            skin.box.normal.textColor = Color.white;

            try
            {
                _view.OnDraw();
            }
            finally
            {
                skin.label.normal.textColor = labelColor;
                skin.button.normal.textColor = buttonColor;
                skin.toggle.normal.textColor = toggleColor;
                skin.box.normal.textColor = boxColor;
            }
        }

        #endregion
    }
}
