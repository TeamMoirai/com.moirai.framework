using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试面板流式构建器。
    /// <para>在 <see cref="DebuggerService.RegisterPanel"/> 回调中声明控件：值控件经 Getter/Setter 闭包绑定（构建期一次性分配），
    /// 运行时由 <c>schedule</c> 按固定间隔轮询 Getter 刷新显示——元素脱离面板时调度自动暂停，无泄漏。</para>
    /// </summary>
    public sealed class DebugPanelBuilder
    {
        #region 常量 [CONSTANTS]

        private const int UPDATE_INTERVAL_MS = 200;

        #endregion

        #region 字段 [FIELDS]

        private readonly List<Action<VisualElement>> _buildActions = new List<Action<VisualElement>>(16);
        private readonly List<Action> _pollActions = new List<Action>(8);

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化调试面板构建器的新实例。
        /// </summary>
        internal DebugPanelBuilder()
        {
        }

        #endregion

        #region 挂载 [ATTACH]

        /// <summary>
        /// 将全部已声明控件构建到目标根元素（由 <see cref="DebugPanel.CreateView"/> 调用）。
        /// </summary>
        /// <param name="root">目标根元素。</param>
        internal void AttachTo(VisualElement root)
        {
            for (int i = 0; i < _buildActions.Count; i++)
            {
                _buildActions[i](root);
            }

            for (int i = 0; i < _pollActions.Count; i++)
            {
                Action update = _pollActions[i];
                root.schedule.Execute(update).Every(UPDATE_INTERVAL_MS);
            }
        }

        #endregion

        #region 布局控件 [LAYOUT CONTROLS]

        /// <summary>
        /// 添加说明标签。
        /// </summary>
        /// <param name="text">标签文本。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddLabel(string text)
        {
            _buildActions.Add(root => root.Add(DebuggerUI.CreateHintLabel(text)));
            return this;
        }

        /// <summary>
        /// 添加分区标题。
        /// </summary>
        /// <param name="title">分区标题。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddSection(string title)
        {
            _buildActions.Add(root => root.Add(DebuggerUI.CreateSectionTitle(title)));
            return this;
        }

        /// <summary>
        /// 添加折叠组（组内控件经嵌套构建器声明，默认折叠）。
        /// </summary>
        /// <param name="title">折叠组标题。</param>
        /// <param name="configure">组内构建回调。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddFoldout(string title, Action<DebugPanelBuilder> configure)
        {
            _buildActions.Add(root =>
            {
                Foldout foldout = new Foldout
                {
                    text = title,
                    value = false
                };
                foldout.style.marginBottom = 4f;

                DebugPanelBuilder nested = new DebugPanelBuilder();
                configure(nested);

                VisualElement container = new VisualElement();
                container.style.flexDirection = FlexDirection.Column;
                foldout.Add(container);
                nested.AttachTo(container);

                root.Add(foldout);
            });
            return this;
        }

        #endregion

        #region 操作控件 [ACTION CONTROLS]

        /// <summary>
        /// 添加操作按钮。
        /// </summary>
        /// <param name="text">按钮文本。</param>
        /// <param name="onClick">点击回调。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddButton(string text, Action onClick)
        {
            _buildActions.Add(root => root.Add(DebuggerUI.CreateActionButton(text, onClick)));
            return this;
        }

        /// <summary>
        /// 添加开关（Getter/Setter 双向绑定）。
        /// </summary>
        /// <param name="label">开关文本。</param>
        /// <param name="getter">取值委托。</param>
        /// <param name="setter">赋值委托。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddToggle(string label, Func<bool> getter, Action<bool> setter)
        {
            _buildActions.Add(root =>
            {
                Toggle toggle = DebuggerUI.CreateToggle(label, getter(), setter);
                root.Add(toggle);
                _pollActions.Add(() =>
                {
                    // 自绘开关的视觉随值变更回调刷新——仅当外部值变化时赋值（跳过时保持视觉）
                    bool currentValue = getter();
                    if (toggle.value != currentValue)
                    {
                        toggle.value = currentValue;
                    }
                });
            });
            return this;
        }

        /// <summary>
        /// 添加滑条（Getter/Setter 双向绑定）。
        /// </summary>
        /// <param name="label">滑条文本。</param>
        /// <param name="min">最小值。</param>
        /// <param name="max">最大值。</param>
        /// <param name="getter">取值委托。</param>
        /// <param name="setter">赋值委托。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddSlider(string label, float min, float max, Func<float> getter, Action<float> setter)
        {
            _buildActions.Add(root =>
            {
                VisualElement row = BuildLabeledRow(label, out Label valueLabel);
                Slider slider = DebuggerUI.CreateSlider(min, max, getter(), value =>
                {
                    setter(value);
                    valueLabel.text = StringUtility.Format("{0:F2}", value);
                });
                row.Add(slider);
                root.Add(row);
                valueLabel.text = StringUtility.Format("{0:F2}", getter());
                _pollActions.Add(() =>
                {
                    if (slider.panel != null)
                    {
                        slider.SetValueWithoutNotify(getter());
                        valueLabel.text = StringUtility.Format("{0:F2}", getter());
                    }
                });
            });
            return this;
        }

        /// <summary>
        /// 添加整型滑条（Getter/Setter 双向绑定）。
        /// </summary>
        /// <param name="label">滑条文本。</param>
        /// <param name="min">最小值。</param>
        /// <param name="max">最大值。</param>
        /// <param name="getter">取值委托。</param>
        /// <param name="setter">赋值委托。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddIntSlider(string label, int min, int max, Func<int> getter, Action<int> setter)
        {
            _buildActions.Add(root =>
            {
                VisualElement row = BuildLabeledRow(label, out Label valueLabel);
                SliderInt slider = new SliderInt(min, max)
                {
                    value = getter()
                };
                slider.style.flexGrow = 1f;
                slider.style.minHeight = 24f;
                slider.RegisterValueChangedCallback(evt =>
                {
                    setter(evt.newValue);
                    valueLabel.text = evt.newValue.ToString();
                });
                row.Add(slider);
                root.Add(row);
                valueLabel.text = getter().ToString();
                _pollActions.Add(() =>
                {
                    if (slider.panel != null)
                    {
                        slider.SetValueWithoutNotify(getter());
                        valueLabel.text = getter().ToString();
                    }
                });
            });
            return this;
        }

        #endregion

        #region 值控件 [VALUE CONTROLS]

        /// <summary>
        /// 添加只读字段（Getter 轮询刷新显示，字符串格式化）。
        /// </summary>
        /// <param name="label">字段标签。</param>
        /// <param name="getter">取值委托。</param>
        /// <param name="format">格式串（如 "{0:F2}"）。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddReadOnlyField(string label, Func<object> getter, string format = "{0}")
        {
            _buildActions.Add(root =>
            {
                VisualElement row = DebuggerUI.CreateRow(label, string.Empty, out Button valueButton);
                root.Add(row);
                void Update()
                {
                    object value = getter();
                    valueButton.text = value == null ? "null" : StringUtility.Format(format, value);
                }

                Update();
                _pollActions.Add(Update);
            });
            return this;
        }

        /// <summary>
        /// 添加进度条（Getter 轮询刷新，值域 min-max）。
        /// </summary>
        /// <param name="label">进度条标签。</param>
        /// <param name="min">最小值。</param>
        /// <param name="max">最大值。</param>
        /// <param name="getter">取值委托。</param>
        /// <returns>本构建器（支持链式调用）。</returns>
        public DebugPanelBuilder AddProgressBar(string label, float min, float max, Func<float> getter)
        {
            _buildActions.Add(root =>
            {
                VisualElement row = new VisualElement();
                row.AddToClassList("dbg-slider-row");

                Label titleLabel = new Label(label);
                titleLabel.AddToClassList("dbg-slider-row__title");

                ProgressBar progressBar = new ProgressBar
                {
                    lowValue = min,
                    highValue = max
                };
                progressBar.style.flexGrow = 1f;
                progressBar.style.height = 22f;
                progressBar.style.marginLeft = 8f;
                progressBar.style.fontSize = 12f;

                row.Add(titleLabel);
                row.Add(progressBar);
                root.Add(row);

                void Update()
                {
                    float value = getter();
                    progressBar.value = value;
                    progressBar.title = StringUtility.Format("{0:F2}", value);
                }

                Update();
                _pollActions.Add(Update);
            });
            return this;
        }

        #endregion

        #region 私有 [PRIVATE]

        private static VisualElement BuildLabeledRow(string label, out Label valueLabel)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-slider-row");

            Label titleLabel = new Label(label);
            titleLabel.AddToClassList("dbg-slider-row__title");
            valueLabel = new Label();
            valueLabel.AddToClassList("dbg-slider-value");
            valueLabel.style.marginLeft = 0f;
            valueLabel.style.marginRight = 8f;

            row.Add(titleLabel);
            row.Add(valueLabel);
            return row;
        }

        #endregion
    }
}
