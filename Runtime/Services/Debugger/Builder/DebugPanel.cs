using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 流式构建的调试面板（<see cref="IDebuggerWindow"/> 实现，由 <see cref="DebugPanelBuilder"/> 声明控件）。
    /// <para>经 <see cref="DebuggerService.RegisterPanel"/> 一行注册——滑条/开关/按钮/折叠组等控件由 Getter/Setter 闭包声明，
    /// 构建期一次性分配，运行时由 <c>schedule</c> 节流轮询刷新（默认 200ms），<c>DetachFromPanelEvent</c> 释放订阅。</para>
    /// </summary>
    public sealed class DebugPanel : IDebuggerWindow
    {
        #region 字段 [FIELDS]

        private readonly string _path;
        private Action<DebugPanelBuilder> _configure;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化调试面板的新实例。
        /// </summary>
        /// <param name="path">面板注册路径（标题取末段）。</param>
        public DebugPanel(string path)
        {
            _path = path ?? string.Empty;
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public void Initialize(params object[] args)
        {
            if (args == null || args.Length < 1 || !(args[0] is Action<DebugPanelBuilder> configure))
            {
                LogUtility.Error("DebugPanel requires an Action<DebugPanelBuilder> argument.");
                return;
            }

            _configure = configure;
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            _configure = null;
        }

        /// <inheritdoc />
        public void OnEnter()
        {
        }

        /// <inheritdoc />
        public void OnLeave()
        {
        }

        /// <inheritdoc />
        public void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <inheritdoc />
        public VisualElement CreateView()
        {
            string title = _path;
            int separator = _path.LastIndexOf('/');
            if (separator >= 0 && separator < _path.Length - 1)
            {
                title = _path.Substring(separator + 1);
            }

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            scrollView.style.paddingLeft = 14f;
            scrollView.style.paddingRight = 14f;
            scrollView.style.paddingTop = 14f;
            scrollView.style.paddingBottom = 14f;
            scrollView.contentContainer.style.flexDirection = FlexDirection.Column;
            DebuggerUI.StyleScrollView(scrollView);

            VisualElement contentRoot = new VisualElement();
            contentRoot.style.flexDirection = FlexDirection.Column;
            scrollView.Add(contentRoot);

            if (!string.IsNullOrEmpty(title))
            {
                contentRoot.Add(DebuggerUI.CreateSectionTitle(title));
            }

            if (_configure != null)
            {
                DebugPanelBuilder builder = new DebugPanelBuilder();
                _configure(builder);
                builder.AttachTo(contentRoot);
            }

            return scrollView;
        }

        #endregion
    }
}
