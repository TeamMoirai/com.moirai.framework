using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器 UI 样式工厂（静态工具，全模块唯一控件构建入口）。
    /// <para>视觉样式统一由共享样式库「Debugger UI.uss」定义（经「Debugger UI Theme.tss」挂载到 PanelSettings）——本工厂只负责结构与 USS 类挂载；悬停/按下/选中三态由 USS 伪类（:hover/:active/:checked）驱动。</para>
    /// <para>所有尺寸均为参考分辨率（1920×1080）面板坐标——实际渲染缩放由 <c>PanelSettings.scale</c> 统一处理，控件构建不感知缩放。</para>
    /// </summary>
    public static class DebuggerUI
    {
        #region 变体枚举 [VARIANTS]

        /// <summary>
        /// 操作按钮语义变体（对应 USS <c>.dbg-button--*</c> 修饰符）。
        /// </summary>
        public enum EButtonStyle
        {
            /// <summary>默认按钮面。</summary>
            Default,

            /// <summary>危险操作（清除/关停）。</summary>
            Danger,

            /// <summary>正向操作（保存/启用）。</summary>
            Positive,

            /// <summary>警示操作（重置布局）。</summary>
            Warning,

            /// <summary>选中态强调（当前档位）。</summary>
            Active,
        }

        /// <summary>
        /// 过滤芯片指示色变体（对应 USS <c>.dbg-chip--*</c> 修饰符注入 <c>--chip-indicator</c>）。
        /// </summary>
        public enum EChipStyle
        {
            /// <summary>信息（主文字色圆点）。</summary>
            Info,

            /// <summary>强调（Accent 圆点）。</summary>
            Accent,

            /// <summary>警告（黄圆点）。</summary>
            Warning,

            /// <summary>错误（红圆点）。</summary>
            Error,

            /// <summary>致命（深红圆点）。</summary>
            Fatal,
        }

        #endregion

        #region 卡片与分区 [CARDS AND SECTIONS]

        /// <summary>
        /// 创建内容卡片（分组容器面板）。
        /// </summary>
        /// <returns>卡片根元素。</returns>
        public static VisualElement CreateCard()
        {
            VisualElement card = new VisualElement();
            card.AddToClassList("dbg-card");
            return card;
        }

        /// <summary>
        /// 创建分区（标题 + 卡片容器）。
        /// </summary>
        /// <param name="title">分区标题（空串则不渲染标题）。</param>
        /// <returns>分区根元素（卡片经 <see cref="VisualElement.contentContainer"/> 追加内容）。</returns>
        public static VisualElement CreateSection(string title)
        {
            return CreateSection(title, out _);
        }

        /// <summary>
        /// 创建分区（标题 + 卡片容器），并输出卡片引用。
        /// </summary>
        /// <param name="title">分区标题（空串则不渲染标题）。</param>
        /// <param name="card">输出的卡片容器。</param>
        /// <returns>分区根元素。</returns>
        public static VisualElement CreateSection(string title, out VisualElement card)
        {
            VisualElement section = new VisualElement();
            section.AddToClassList("dbg-section");

            if (!string.IsNullOrEmpty(title))
            {
                section.Add(CreateSectionTitle(title));
            }

            card = CreateCard();
            section.Add(card);
            return section;
        }

        /// <summary>
        /// 创建分区标题（加粗主文字）。
        /// </summary>
        /// <param name="title">分区标题。</param>
        /// <returns>标题元素。</returns>
        public static Label CreateSectionTitle(string title)
        {
            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("dbg-section__title");
            return titleLabel;
        }

        /// <summary>
        /// 创建说明文字（次要色，可换行）。
        /// </summary>
        /// <param name="text">说明内容。</param>
        /// <returns>说明元素。</returns>
        public static Label CreateHintLabel(string text)
        {
            Label label = new Label(text);
            label.AddToClassList("dbg-hint");
            return label;
        }

        #endregion

        #region 键值行 [KEY-VALUE ROWS]

        /// <summary>
        /// 创建键值行（值区域点击复制；值按钮引用经 <paramref name="valueButton"/> 输出以便轮询刷新——Button 自身继承 TextElement，直接写 text 即更新显示）。
        /// </summary>
        /// <param name="title">左侧标签。</param>
        /// <param name="content">右侧取值初始文本。</param>
        /// <param name="valueButton">输出的值按钮引用（可为 null——调用方无需跟踪时省略）。</param>
        /// <returns>行根元素。</returns>
        public static VisualElement CreateRow(string title, string content, out Button valueButton)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-row");

            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("dbg-row__title");

            valueButton = CreateGhostButton(content);
            row.Add(titleLabel);
            row.Add(valueButton);
            return row;
        }

        /// <summary>
        /// 创建键值行（值区域点击复制）。
        /// </summary>
        /// <param name="title">左侧标签。</param>
        /// <param name="content">右侧取值初始文本。</param>
        /// <returns>行根元素。</returns>
        public static VisualElement CreateRow(string title, string content)
        {
            return CreateRow(title, content, out _);
        }

        /// <summary>
        /// 创建比例分配宽度的键值行（标题与值按 2:1 比例分摊行宽，适配长定位地址清单）。
        /// </summary>
        /// <param name="title">左侧标签。</param>
        /// <param name="content">右侧取值初始文本。</param>
        /// <returns>行根元素。</returns>
        public static VisualElement CreateRow(string title, string content, float titleWidthRatio)
        {
            return CreateRow(title, content, titleWidthRatio, out _);
        }

        /// <summary>
        /// 创建比例分配宽度的键值行，并输出值按钮引用。
        /// </summary>
        /// <param name="title">左侧标签。</param>
        /// <param name="content">右侧取值初始文本。</param>
        /// <param name="titleWidthRatio">标题宽度占比（仅识别 2/3 一档，其余按默认行处理）。</param>
        /// <param name="valueButton">输出的值按钮引用。</param>
        /// <returns>行根元素。</returns>
        public static VisualElement CreateRow(string title, string content, float titleWidthRatio, out Button valueButton)
        {
            VisualElement row = CreateRow(title, content, out valueButton);
            if (Mathf.Approximately(titleWidthRatio, 2f / 3f))
            {
                row.AddToClassList("dbg-row--wide");
            }

            return row;
        }

        #endregion

        #region 按钮 [BUTTONS]

        /// <summary>
        /// 创建操作按钮（三态由 USS <c>.unity-button</c> 伪类驱动）。
        /// </summary>
        /// <param name="text">按钮文本。</param>
        /// <param name="onClick">点击回调。</param>
        /// <param name="style">语义变体（默认按钮面）。</param>
        /// <returns>按钮元素。</returns>
        public static Button CreateActionButton(string text, Action onClick, EButtonStyle style = EButtonStyle.Default)
        {
            Button button = new Button(onClick)
            {
                text = text ?? string.Empty
            };
            button.AddToClassList("dbg-button");
            switch (style)
            {
                case EButtonStyle.Danger:
                    button.AddToClassList("dbg-button--danger");
                    break;

                case EButtonStyle.Positive:
                    button.AddToClassList("dbg-button--positive");
                    break;

                case EButtonStyle.Warning:
                    button.AddToClassList("dbg-button--warning");
                    break;

                case EButtonStyle.Active:
                    button.AddToClassList("dbg-button--active");
                    break;
            }

            return button;
        }

        /// <summary>
        /// 创建工具栏按钮（28px 紧凑高度，与其他工具栏控件基线对齐）。
        /// </summary>
        /// <param name="text">按钮文本。</param>
        /// <param name="onClick">点击回调。</param>
        /// <param name="style">语义变体。</param>
        /// <returns>按钮元素。</returns>
        public static Button CreateToolbarButton(string text, Action onClick, EButtonStyle style = EButtonStyle.Default)
        {
            Button button = CreateActionButton(text, onClick, style);
            button.AddToClassList("dbg-button--toolbar");
            return button;
        }

        /// <summary>
        /// 创建幽灵按钮（透明底、左对齐文本——键值行值区域与内联操作）。
        /// </summary>
        /// <param name="text">按钮文本。</param>
        /// <returns>按钮元素。</returns>
        public static Button CreateGhostButton(string text)
        {
            Button button = new Button
            {
                text = text ?? string.Empty
            };
            button.AddToClassList("dbg-row__value");
            button.clicked += () => CopyToClipboard(text);
            return button;
        }

        #endregion

        #region 开关与芯片 [TOGGLES AND CHIPS]

        /// <summary>
        /// 创建开关（标准 Toggle 复选框——勾选框与文字样式经共享样式库 <c>.dbg-toggle</c> 主题化，选中态由 USS <c>:checked</c> 伪类驱动）。
        /// </summary>
        /// <param name="label">开关文本。</param>
        /// <param name="value">初始值。</param>
        /// <param name="onValueChanged">值变更回调。</param>
        /// <returns>开关元素。</returns>
        public static Toggle CreateToggle(string label, bool value, Action<bool> onValueChanged)
        {
            Toggle toggle = new Toggle(label)
            {
                value = value
            };
            toggle.AddToClassList("dbg-toggle");
            toggle.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
            return toggle;
        }

        /// <summary>
        /// 创建控制台过滤器芯片（圆点指示色 + 计数文本，选中态由 USS <c>:checked</c> 驱动）。
        /// </summary>
        /// <param name="label">芯片文本（不含计数）。</param>
        /// <param name="value">初始启用值。</param>
        /// <param name="chipStyle">指示色变体。</param>
        /// <param name="onValueChanged">值变更回调。</param>
        /// <returns>芯片元素。</returns>
        public static Toggle CreateFilterChip(string label, bool value, EChipStyle chipStyle, Action<bool> onValueChanged)
        {
            Toggle toggle = new Toggle
            {
                value = value
            };
            toggle.AddToClassList("dbg-chip");
            switch (chipStyle)
            {
                case EChipStyle.Accent:
                    toggle.AddToClassList("dbg-chip--accent");
                    break;

                case EChipStyle.Warning:
                    toggle.AddToClassList("dbg-chip--warning");
                    break;

                case EChipStyle.Error:
                    toggle.AddToClassList("dbg-chip--danger");
                    break;

                case EChipStyle.Fatal:
                    toggle.AddToClassList("dbg-chip--fatal");
                    break;
            }

            VisualElement dot = new VisualElement();
            dot.AddToClassList("dbg-chip__dot");
            dot.pickingMode = PickingMode.Ignore;
            toggle.Insert(0, dot);

            Label textLabel = new Label(label);
            textLabel.AddToClassList("dbg-chip__text");
            textLabel.pickingMode = PickingMode.Ignore;
            toggle.Add(textLabel);

            toggle.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
            return toggle;
        }

        /// <summary>
        /// 更新过滤器芯片计数文本。
        /// </summary>
        /// <param name="chip">芯片元素。</param>
        /// <param name="baseText">芯片基础文本（不含计数）。</param>
        /// <param name="count">当前计数。</param>
        public static void UpdateFilterChipText(Toggle chip, string baseText, int count)
        {
            Label textLabel = chip.Q<Label>(className: "unity-label");
            if (textLabel == null)
            {
                return;
            }

            textLabel.text = StringUtility.Format("{0} ({1})", baseText, count);
        }

        #endregion

        #region 滑条与文本 [SLIDERS AND TEXT]

        /// <summary>
        /// 创建滑条（外观经共享样式库主题化，与内置滑条一致）。
        /// </summary>
        /// <param name="min">最小值。</param>
        /// <param name="max">最大值。</param>
        /// <param name="value">初始值。</param>
        /// <param name="onValueChanged">值变更回调。</param>
        /// <returns>滑条元素。</returns>
        public static Slider CreateSlider(float min, float max, float value, Action<float> onValueChanged)
        {
            Slider slider = new Slider(min, max)
            {
                value = value
            };
            slider.style.flexGrow = 1f;
            slider.style.minHeight = 24f;
            slider.RegisterValueChangedCallback(evt => onValueChanged?.Invoke(evt.newValue));
            return slider;
        }

        /// <summary>
        /// 创建只读多行文本（堆栈等长文本展示）。
        /// </summary>
        /// <param name="value">初始文本。</param>
        /// <returns>文本元素。</returns>
        public static TextField CreateReadOnlyMultilineText(string value)
        {
            TextField textField = new TextField
            {
                multiline = true,
                isReadOnly = true,
                value = value ?? string.Empty
            };
            textField.AddToClassList("dbg-multiline");
            return textField;
        }

        /// <summary>
        /// 创建工具栏行（横向自动换行容器，内容居左）。
        /// </summary>
        /// <returns>工具栏根元素。</returns>
        public static VisualElement CreateToolbarRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-toolbar");
            return row;
        }

        /// <summary>
        /// 创建搜索框（占位符 + 聚焦闪烁光标：失焦无文本显示提示，聚焦无文本光标 500ms 方波闪烁，有文本隐藏占位符仅显示输入）。
        /// <para>聚焦/失焦/文本变更**当帧立即**刷新占位符状态（事件驱动），500ms 定时仅驱动闪烁相位——无恢复延迟。</para>
        /// </summary>
        /// <param name="placeholderText">占位符提示文本。</param>
        /// <param name="onValueChanged">文本变更回调（null 安全）。</param>
        /// <returns>搜索框根元素（含输入框与占位符叠加层；样式类由调用方按位置追加）。</returns>
        public static TextField CreateSearchField(string placeholderText, Action<string> onValueChanged)
        {
            const string searchFieldCaret = "|";

            TextField searchField = new TextField();

            Label placeholder = new Label(placeholderText) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("dbg-search-placeholder");

            bool IsFocused()
            {
                return searchField.focusController != null && searchField.focusController.focusedElement == searchField;
            }

            // 立即刷新：聚焦（光标相位）/失焦（提示或隐藏）/文本变更（编辑中隐藏，清空回光标）——零延迟响应
            void UpdatePlaceholder(bool blinkOn)
            {
                if (IsFocused())
                {
                    if (string.IsNullOrEmpty(searchField.text))
                    {
                        placeholder.text = blinkOn ? searchFieldCaret : string.Empty;
                        placeholder.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        placeholder.style.display = DisplayStyle.None;
                    }
                }
                else
                {
                    placeholder.text = placeholderText;
                    placeholder.style.display = string.IsNullOrEmpty(searchField.text) ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            searchField.RegisterCallback<FocusInEvent>(_ => UpdatePlaceholder(true));
            searchField.RegisterCallback<FocusOutEvent>(_ => UpdatePlaceholder(false));

            // 500ms 定时仅驱动闪烁相位（tick 内自检 panel==null 跳过——勿用 DetachFromPanelEvent+Pause，
            // Pause 单向无恢复，视图重挂后动画永久停摆：Console 聚焦不闪的根因）
            placeholder.schedule.Execute(() =>
            {
                if (placeholder.panel == null)
                {
                    return;
                }

                UpdatePlaceholder((Mathf.FloorToInt(Time.unscaledTime * 2f) & 1) == 0);
            }).Every(500L);

            searchField.RegisterValueChangedCallback(evt =>
            {
                UpdatePlaceholder(IsFocused());
                onValueChanged?.Invoke(evt.newValue ?? string.Empty);
            });

            VisualElement wrapper = new VisualElement { name = "debugger-search-field" };
            wrapper.AddToClassList("dbg-search-wrap");
            wrapper.Add(searchField);
            wrapper.Add(placeholder);
            searchField.userData = wrapper;
            return searchField;
        }

        #endregion

        #region 滚动视图 [SCROLL VIEWS]

        /// <summary>
        /// 统一滚动视图样式（滚动条外观经共享样式库主题化，此处仅设滚动速度与挂类）。
        /// </summary>
        /// <param name="scrollView">目标滚动视图。</param>
        public static void StyleScrollView(ScrollView scrollView)
        {
            if (scrollView == null)
            {
                return;
            }

            scrollView.AddToClassList("dbg-scroll");
            scrollView.mouseWheelScrollSize = 240f;
        }

        #endregion

        #region 工具 [UTILITIES]

        /// <summary>
        /// 复制文本到系统剪贴板。
        /// </summary>
        /// <param name="content">要复制的内容。</param>
        public static void CopyToClipboard(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = content;
        }

        /// <summary>
        /// 格式化字节长度为可读字符串（Bytes 到 EB）。
        /// </summary>
        /// <param name="byteLength">字节长度。</param>
        /// <returns>可读字符串。</returns>
        public static string GetByteLengthString(long byteLength)
        {
            if (byteLength < 1024L)
            {
                return StringUtility.Format("{0} Bytes", byteLength);
            }

            if (byteLength < 1048576L)
            {
                return StringUtility.Format("{0:F2} KB", byteLength / 1024f);
            }

            if (byteLength < 1073741824L)
            {
                return StringUtility.Format("{0:F2} MB", byteLength / 1048576f);
            }

            if (byteLength < 1099511627776L)
            {
                return StringUtility.Format("{0:F2} GB", byteLength / 1073741824f);
            }

            if (byteLength < 1125899906842624L)
            {
                return StringUtility.Format("{0:F2} TB", byteLength / 1099511627776f);
            }

            if (byteLength < 1152921504606846976L)
            {
                return StringUtility.Format("{0:F2} PB", byteLength / 1125899906842624f);
            }

            return StringUtility.Format("{0:F2} EB", byteLength / 1152921504606846976f);
        }

        /// <summary>
        /// 格式化字节长度为紧凑统计字符串（B/KB/MB/GB）。
        /// </summary>
        /// <param name="byteLength">字节长度。</param>
        /// <returns>紧凑字符串。</returns>
        public static string GetCompactByteString(long byteLength)
        {
            if (byteLength < 1024L)
            {
                return StringUtility.Format("{0} B", byteLength);
            }

            if (byteLength < 1048576L)
            {
                return StringUtility.Format("{0:F1} KB", byteLength / 1024f);
            }

            if (byteLength < 1073741824L)
            {
                return StringUtility.Format("{0:F1} MB", byteLength / 1048576f);
            }

            return StringUtility.Format("{0:F2} GB", byteLength / 1073741824f);
        }

        /// <summary>
        /// 格式化计数为紧凑字符串（K/M）。
        /// </summary>
        /// <param name="value">计数值（负数为不可用）。</param>
        /// <returns>紧凑字符串。</returns>
        public static string GetCompactCountString(long value)
        {
            if (value < 0L)
            {
                return "n/a";
            }

            if (value >= 1000000L)
            {
                return StringUtility.Format("{0:F2}M", value / 1000000f);
            }

            if (value >= 1000L)
            {
                return StringUtility.Format("{0:F1}K", value / 1000f);
            }

            return value.ToString();
        }

        #endregion
    }
}
