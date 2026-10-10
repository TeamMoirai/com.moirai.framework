using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器自身设置窗口（窗口缩放、常驻统计 HUD、布局重置）。
    /// </summary>
    public sealed class SettingsWindow : ScrollableDebuggerWindowBase
    {
        #region 字段 [FIELDS]

        private Label _scaleLabel;

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            BuildLayoutSection(root);
            BuildDisplaySection(root);
        }

        #endregion

        #region 分区 [SECTIONS]

        private void BuildLayoutSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Window Layout");
            card.Add(DebuggerUI.CreateHintLabel("Drag the window caption to move; drag the bottom-right handle to resize."));

            _scaleLabel = new Label(StringUtility.Format("UI Scale: {0:F2}", GetHostScale()));
            _scaleLabel.AddToClassList("dbg-slider-row__title");
            _scaleLabel.style.marginBottom = 4f;
            card.Add(_scaleLabel);

            Slider scaleSlider = DebuggerUI.CreateSlider(0.5f, 2f, GetHostScale(), value =>
            {
                DebuggerRuntimeHost host = DebuggerRuntimeHost.Instance;
                if (host != null)
                {
                    host.WindowScale = value;
                }

                if (_scaleLabel != null)
                {
                    _scaleLabel.text = StringUtility.Format("UI Scale: {0:F2}", value);
                }
            });
            card.Add(scaleSlider);

            VisualElement presetRow = DebuggerUI.CreateToolbarRow();
            presetRow.Add(DebuggerUI.CreateActionButton("0.75x", () => SetHostScale(0.75f)));
            presetRow.Add(DebuggerUI.CreateActionButton("1.0x", () => SetHostScale(1f)));
            presetRow.Add(DebuggerUI.CreateActionButton("1.25x", () => SetHostScale(1.25f)));
            presetRow.Add(DebuggerUI.CreateActionButton("1.5x", () => SetHostScale(1.5f)));
            card.Add(presetRow);

            card.Add(DebuggerUI.CreateActionButton("Reset Layout", () => DebuggerRuntimeHost.Instance?.ResetLayout(), DebuggerUI.EButtonStyle.Warning));
        }

        private void BuildDisplaySection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Display");
            DebuggerRuntimeHost host = DebuggerRuntimeHost.Instance;
            bool statsVisible = host != null && host.StatsOverlayVisible;
            card.Add(DebuggerUI.CreateToggle("Show Stats Overlay (FPS / Memory)", statsVisible, value =>
            {
                if (DebuggerRuntimeHost.Instance != null)
                {
                    DebuggerRuntimeHost.Instance.StatsOverlayVisible = value;
                }
            }));
        }

        #endregion

        #region 私有 [PRIVATE]

        private static float GetHostScale()
        {
            return DebuggerRuntimeHost.Instance != null ? DebuggerRuntimeHost.Instance.WindowScale : 1f;
        }

        private static void SetHostScale(float scale)
        {
            if (DebuggerRuntimeHost.Instance != null)
            {
                DebuggerRuntimeHost.Instance.WindowScale = scale;
            }
        }

        #endregion
    }
}
