using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 服务调试视图抽象基类。
    /// <para>定义单个服务的调试内容并以 GUILayout 绘制，与渲染宿主解耦——同一视图可渲染于编辑器 Inspector（IMGUI）或游戏内调试器窗口（经 <see cref="CreateView"/> 默认实现以 <c>IMGUIContainer</c> 嵌入 UI Toolkit 面板）。</para>
    /// <para>实现 <see cref="IDebuggerWindow"/>，可经 <see cref="DebuggerService.RegisterDebugView"/> 直接注册进游戏内调试器；各宿主应持有独立实例（滚动位置等绘制状态不共享）。派生类可覆写 <see cref="CreateView"/> 提供原生 UI Toolkit 视图。</para>
    /// </summary>
    public abstract class ServiceDebugView : IDebuggerWindow
    {
        #region 常量 [CONSTANTS]

        /// <summary>
        /// 键值行左侧标签的固定宽度（派生类可据此推导自定义行宽）。
        /// </summary>
        protected const float ITEM_LABEL_WIDTH = 146f;

        private const float PROGRESS_BAR_HEIGHT = 18f;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 调试视图标题（宿主窗口与 Inspector 分区的显示名）。
        /// </summary>
        public abstract string Title { get; }

        /// <summary>
        /// 服务是否就绪——未就绪时仅绘制提示信息而不绘制调试内容。
        /// </summary>
        public abstract bool IsReady { get; }

        /// <summary>
        /// 服务未就绪时显示的提示文案。
        /// </summary>
        protected virtual string NotReadyMessage => "服务未就绪（需进入运行时并完成初始化）。";

        #endregion

        #region 窗口生命周期 [WINDOW LIFECYCLE]

        /// <inheritdoc />
        public virtual void Initialize(params object[] args)
        {
        }

        /// <inheritdoc />
        public virtual void Shutdown()
        {
        }

        /// <inheritdoc />
        public virtual void OnEnter()
        {
        }

        /// <inheritdoc />
        public virtual void OnLeave()
        {
        }

        /// <inheritdoc />
        public virtual void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <inheritdoc />
        /// <remarks>默认实现经 <see cref="IMGUIDebuggerWindow"/> 将 <see cref="OnDraw"/> 的 GUILayout 内容嵌入 UI Toolkit 面板；派生类可覆写提供原生 UI Toolkit 视图。</remarks>
        public virtual VisualElement CreateView()
        {
            return new IMGUIDebuggerWindow(this).CreateView();
        }

        #endregion

        #region IMGUI 绘制 [IMGUI DRAW]

        /// <summary>
        /// IMGUI 绘制入口（编辑器 Inspector 与游戏内 <c>IMGUIContainer</c> 共用）。
        /// </summary>
        public void OnDraw()
        {
            DrawSectionTitle(Title);
            if (!IsReady)
            {
                DrawHelpBox(NotReadyMessage, false);
                return;
            }

            OnDrawContent();
        }

        #endregion

        #region 绘制契约 [DRAW CONTRACT]

        /// <summary>
        /// 绘制调试内容（服务就绪时由 <see cref="OnDraw"/> 调用）。
        /// </summary>
        protected abstract void OnDrawContent();

        #endregion

        #region 绘制辅助 [DRAW UTILITIES]

        /// <summary>
        /// 绘制分区标题（加粗标签）。
        /// </summary>
        /// <param name="title">分区标题。</param>
        protected static void DrawSectionTitle(string title)
        {
            GUILayout.Label(title, BoldLabelStyle);
        }

        /// <summary>
        /// 绘制键值行（点击取值复制到剪贴板）。
        /// </summary>
        /// <param name="label">左侧标签。</param>
        /// <param name="value">右侧取值。</param>
        protected static void DrawItem(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(ITEM_LABEL_WIDTH));
            if (GUILayout.Button(value, "label"))
            {
                GUIUtility.systemCopyBuffer = value;
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制提示框（盒装文本，警告样式加粗）。
        /// </summary>
        /// <param name="message">提示内容。</param>
        /// <param name="warning">是否为警告样式。</param>
        protected static void DrawHelpBox(string message, bool warning)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label(message, warning ? BoldLabelStyle : GUI.skin.label);
            GUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制使用率进度条（背景框 + 填充色块 + 居中文本）。
        /// </summary>
        /// <param name="label">左侧标签。</param>
        /// <param name="ratio">取值比率（自动钳制到 0-1）。</param>
        /// <param name="text">进度条内显示的文本。</param>
        protected static void DrawProgressBar(string label, float ratio, string text)
        {
            float clampedRatio = Mathf.Clamp01(ratio);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(ITEM_LABEL_WIDTH));
            Rect barRect = GUILayoutUtility.GetRect(18f, PROGRESS_BAR_HEIGHT, GUILayout.ExpandWidth(true));
            DrawProgressBarFill(barRect, clampedRatio, text);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制进度条本体（由 <see cref="DrawProgressBar"/> 调用）。
        /// </summary>
        /// <param name="rect">进度条矩形区域。</param>
        /// <param name="ratio">已钳制的取值比率。</param>
        /// <param name="text">进度条内显示的文本。</param>
        private static void DrawProgressBarFill(Rect rect, float ratio, string text)
        {
            GUI.Box(rect, GUIContent.none);
            if (ratio > 0f)
            {
                Color cachedColor = GUI.color;
                GUI.color = PROGRESS_FILL_COLOR;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * ratio, rect.height), Texture2D.whiteTexture);
                GUI.color = cachedColor;
            }

            GUI.Label(rect, text, CenteredLabelStyle);
        }

        #endregion

        #region 缓存样式 [CACHED STYLES]

        private static readonly Color PROGRESS_FILL_COLOR = new Color(0.26f, 0.62f, 0.28f, 1f);

        private static GUIStyle s_BoldLabelStyle;
        private static GUISkin s_BoldLabelStyleSkin;

        /// <summary>
        /// 加粗标签样式（按当前皮肤缓存，Inspector 与游戏内皮肤切换时自动重建）。
        /// </summary>
        private static GUIStyle BoldLabelStyle
        {
            get
            {
                GUISkin currentSkin = GUI.skin;
                if (s_BoldLabelStyle == null || !ReferenceEquals(s_BoldLabelStyleSkin, currentSkin))
                {
                    s_BoldLabelStyle = new GUIStyle(currentSkin.label) { fontStyle = FontStyle.Bold };
                    s_BoldLabelStyleSkin = currentSkin;
                }

                return s_BoldLabelStyle;
            }
        }

        private static GUIStyle s_CenteredLabelStyle;
        private static GUISkin s_CenteredLabelStyleSkin;

        /// <summary>
        /// 居中标签样式（按当前皮肤缓存，用于进度条内文本）。
        /// </summary>
        private static GUIStyle CenteredLabelStyle
        {
            get
            {
                GUISkin currentSkin = GUI.skin;
                if (s_CenteredLabelStyle == null || !ReferenceEquals(s_CenteredLabelStyleSkin, currentSkin))
                {
                    s_CenteredLabelStyle = new GUIStyle(currentSkin.label) { alignment = TextAnchor.MiddleCenter };
                    s_CenteredLabelStyleSkin = currentSkin;
                }

                return s_CenteredLabelStyle;
            }
        }

        #endregion
    }
}
