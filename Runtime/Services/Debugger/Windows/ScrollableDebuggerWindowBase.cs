using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 可滚动调试器窗口基类（UI Toolkit）。
    /// <para><see cref="CreateView"/> 构建统一样式的垂直滚动视图并委托 <see cref="BuildWindow"/> 填充内容；派生类经 <see cref="Rebuild"/> 重建内容（采样、轮询刷新场景）。</para>
    /// </summary>
    public abstract class ScrollableDebuggerWindowBase : IDebuggerWindow
    {
        #region 字段 [FIELDS]

        private ScrollView _scrollView;
        private VisualElement _contentRoot;

        #endregion

        #region 生命周期 [LIFECYCLE]

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
        public virtual VisualElement CreateView()
        {
            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1f;
            _scrollView.style.flexShrink = 1f;
            _scrollView.style.paddingLeft = 14f;
            _scrollView.style.paddingRight = 14f;
            _scrollView.style.paddingTop = 14f;
            _scrollView.style.paddingBottom = 14f;
            _scrollView.contentContainer.style.flexDirection = FlexDirection.Column;
            _scrollView.usageHints = UsageHints.DynamicTransform;
            DebuggerUI.StyleScrollView(_scrollView);

            _contentRoot = new VisualElement();
            _contentRoot.style.flexDirection = FlexDirection.Column;
            _contentRoot.usageHints = UsageHints.DynamicTransform;
            _scrollView.Add(_contentRoot);

            BuildWindow(_contentRoot);
            return _scrollView;
        }

        #endregion

        #region 构建契约 [BUILD CONTRACT]

        /// <summary>
        /// 构建窗口内容（由 <see cref="CreateView"/> 调用，<see cref="Rebuild"/> 重建时再次调用）。
        /// </summary>
        /// <param name="root">内容根元素。</param>
        protected abstract void BuildWindow(VisualElement root);

        /// <summary>
        /// 重建窗口内容（清空后重新 <see cref="BuildWindow"/>）。
        /// </summary>
        protected void Rebuild()
        {
            if (_contentRoot == null)
            {
                return;
            }

            _contentRoot.Clear();
            BuildWindow(_contentRoot);
        }

        #endregion

        #region 辅助 [UTILITIES]

        /// <summary>
        /// 在内容根追加一个分区（标题 + 卡片），返回卡片以便追加子元素。
        /// </summary>
        /// <param name="root">内容根元素。</param>
        /// <param name="title">分区标题（空串省略标题）。</param>
        /// <returns>卡片容器元素。</returns>
        protected static VisualElement AddSection(VisualElement root, string title)
        {
            DebuggerUI.CreateSection(title, out VisualElement card);
            root.Add(card.parent);
            return card;
        }

        /// <summary>
        /// 在指定容器追加一个键值行（值区域点击复制）。
        /// </summary>
        /// <param name="container">目标容器。</param>
        /// <param name="title">左侧标签。</param>
        /// <param name="content">右侧取值。</param>
        protected static void AddRow(VisualElement container, string title, string content)
        {
            container.Add(DebuggerUI.CreateRow(title, content));
        }

        /// <summary>
        /// 在指定容器追加一个带值按钮引用的键值行（供后续刷新——Button 继承 TextElement，直接写 text 即更新）。
        /// </summary>
        /// <param name="container">目标容器。</param>
        /// <param name="title">左侧标签。</param>
        /// <param name="content">右侧取值初始文本。</param>
        /// <param name="valueButton">输出的值按钮引用。</param>
        protected static void AddRow(VisualElement container, string title, string content, out Button valueButton)
        {
            container.Add(DebuggerUI.CreateRow(title, content, out valueButton));
        }

        /// <summary>
        /// 在指定容器追加一个比例分配宽度的键值行（长清单视图用，标题与值按占比分摊行宽）。
        /// </summary>
        /// <param name="container">目标容器。</param>
        /// <param name="title">左侧标签。</param>
        /// <param name="content">右侧取值初始文本。</param>
        /// <param name="titleWidthRatio">标题宽度占比（0-1）。</param>
        protected static void AddRow(VisualElement container, string title, string content, float titleWidthRatio)
        {
            container.Add(DebuggerUI.CreateRow(title, content, titleWidthRatio));
        }

        #endregion
    }

    /// <summary>
    /// 轮询刷新的调试器窗口基类（按固定间隔重建内容——仅窗口可见期间驱动）。
    /// <para>用于运行时状态信息窗口（Screen/Scene/Time/Profiler 等）：免除逐行 Getter 闭包与每帧分配，统一以重建节流。</para>
    /// </summary>
    public abstract class PollingDebuggerWindowBase : ScrollableDebuggerWindowBase
    {
        #region 字段 [FIELDS]

        private readonly float _refreshInterval;
        private float _countdown;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化轮询窗口基类的新实例。
        /// </summary>
        /// <param name="refreshInterval">刷新间隔（秒，最小 0.05）。</param>
        protected PollingDebuggerWindowBase(float refreshInterval = 0.25f)
        {
            _refreshInterval = Mathf.Max(0.05f, refreshInterval);
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override void OnEnter()
        {
            _countdown = 0f;
            Rebuild();
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            _countdown -= realElapseSeconds;
            if (_countdown > 0f)
            {
                return;
            }

            _countdown = _refreshInterval;
            Rebuild();
        }

        #endregion
    }
}
