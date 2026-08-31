using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 游戏应用设置窗口（FrameRate / GameSpeed 实时控制与本地设置键值清单）。
    /// <para>整合原 GameAppEditor 调试信息（GameApp 去 MonoBehaviour 化后的 Inspector 调试入口承接）。</para>
    /// </summary>
    public sealed class GameAppInformationWindow : PollingDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private static readonly float[] s_GameSpeedPresets = { 0f, 0.01f, 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f, 4f, 8f };
        private static readonly string[] s_GameSpeedLabels = { "0x", "0.01x", "0.1x", "0.25x", "0.5x", "1x", "1.5x", "2x", "4x", "8x" };

        private const int MIN_FRAME_RATE = 1;
        private const int MAX_FRAME_RATE = 300;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化游戏应用设置窗口的新实例。
        /// </summary>
        public GameAppInformationWindow() : base(1f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            BuildRuntimeControls(root);
            BuildSettingStore(root);
        }

        #endregion

        #region 分区 [SECTIONS]

        private void BuildRuntimeControls(VisualElement root)
        {
            VisualElement card = AddSection(root, "运行时控制 [RUNTIME CONTROLS]");

            AddRow(card, "游戏是否暂停 [Is Paused]", GameAppSettings.IsGamePaused.ToString());
            AddRow(card, "框架运行状态 [Framework]", GameApp.IsShutdown ? "Shutdown" : "Active");

            card.Add(BuildFrameRateRow());
            card.Add(BuildGameSpeedRow());

            VisualElement presetRow = DebuggerUI.CreateToolbarRow();
            for (int i = 0; i < s_GameSpeedPresets.Length; i++)
            {
                int index = i;
                presetRow.Add(DebuggerUI.CreateActionButton(s_GameSpeedLabels[i], () =>
                {
                    GameAppSettings.GameSpeed = s_GameSpeedPresets[index];
                    Rebuild();
                }, IsSelectedSpeed(s_GameSpeedPresets[i]) ? DebuggerUI.EButtonStyle.Active : DebuggerUI.EButtonStyle.Default));
            }

            card.Add(presetRow);
        }

        private void BuildSettingStore(VisualElement root)
        {
            VisualElement card = AddSection(root, "本地设置 [SETTING STORE]");
            int count = SettingUtility.Count;
            if (count < 0)
            {
                card.Add(DebuggerUI.CreateHintLabel("<Unknown>"));
                return;
            }

            AddRow(card, "设置项数量 [Count]", count.ToString());
            if (count > 0)
            {
                string[] settingNames = SettingUtility.GetAllSettingNames();
                for (int i = 0; i < settingNames.Length; i++)
                {
                    AddRow(card, settingNames[i], SettingUtility.GetString(settingNames[i]));
                }
            }

            VisualElement buttonRow = DebuggerUI.CreateToolbarRow();
            buttonRow.Add(DebuggerUI.CreateActionButton("Save Settings", () => SettingUtility.Save(), DebuggerUI.EButtonStyle.Positive));
            buttonRow.Add(DebuggerUI.CreateActionButton("Remove All Settings", () =>
            {
                SettingUtility.RemoveAllSettings();
                Rebuild();
            }, DebuggerUI.EButtonStyle.Danger));
            card.Add(buttonRow);
        }

        #endregion

        #region 私有 [PRIVATE]

        private static VisualElement BuildFrameRateRow()
        {
            VisualElement row = NewSliderRow("目标帧率 [Frame Rate]");

            SliderInt slider = new SliderInt(MIN_FRAME_RATE, MAX_FRAME_RATE)
            {
                value = GameAppSettings.FrameRate
            };
            slider.style.flexGrow = 1f;
            slider.style.minHeight = 24f;

            Label valueLabel = NewSliderValueLabel();
            valueLabel.text = StringUtility.Format("{0} FPS", GameAppSettings.FrameRate);
            slider.RegisterValueChangedCallback(evt =>
            {
                GameAppSettings.FrameRate = evt.newValue;
                valueLabel.text = StringUtility.Format("{0} FPS", evt.newValue);
            });

            row.Add(slider);
            row.Add(valueLabel);
            return row;
        }

        private static VisualElement BuildGameSpeedRow()
        {
            VisualElement row = NewSliderRow("游戏速度 [Game Speed]");

            // 默认主题滑条（与目标帧率行同款外观）——自绘样式曾与默认主题不一致
            Slider slider = new Slider(0f, 8f)
            {
                value = GameAppSettings.GameSpeed
            };
            slider.style.flexGrow = 1f;
            slider.style.minHeight = 24f;

            Label valueLabel = NewSliderValueLabel();
            valueLabel.text = StringUtility.Format("{0:F2}x", GameAppSettings.GameSpeed);
            slider.RegisterValueChangedCallback(evt =>
            {
                GameAppSettings.GameSpeed = evt.newValue;
                valueLabel.text = StringUtility.Format("{0:F2}x", evt.newValue);
            });

            row.Add(slider);
            row.Add(valueLabel);
            return row;
        }

        private static VisualElement NewSliderRow(string title)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-slider-row");

            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("dbg-slider-row__title");
            row.Add(titleLabel);
            return row;
        }

        private static Label NewSliderValueLabel()
        {
            Label valueLabel = new Label();
            valueLabel.AddToClassList("dbg-slider-value");
            return valueLabel;
        }

        private static bool IsSelectedSpeed(float speed)
        {
            return Math.Abs(GameAppSettings.GameSpeed - speed) < 0.001f;
        }

        #endregion
    }
}
