using System;
using Moirai.Atropos.Debugger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频服务调试视图（原生 UI Toolkit，经 <see cref="AudioService.OnInit"/> 注册进游戏内调试器 "Profiler/Audio"）。
    /// <para>提供主音量与各音轨（枚举全量生成）音量/静音/暂停/恢复/停止实时控制，并支持写入音频设置。</para>
    /// </summary>
    public sealed class AudioServiceDebuggerWindow : ScrollableDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!AudioService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Audio Service"));
                root.Add(DebuggerUI.CreateHintLabel("音频服务未就绪（需进入运行时并完成初始化）。"));
                return;
            }

            VisualElement masterCard = AddSection(root, "主音量 [MASTER VOLUME]");
            AddMasterControls(masterCard);

            VisualElement trackCard = AddSection(root, "音轨 [TRACKS]");
            var values = (EAudioTrack[])Enum.GetValues(typeof(EAudioTrack));
            for (int i = 0; i < values.Length; i++)
            {
                AddTrackControls(trackCard, values[i]);
            }

            VisualElement settingsCard = AddSection(root, "设置 [SETTINGS]");
            VisualElement settingsRow = DebuggerUI.CreateToolbarRow();
            settingsRow.Add(DebuggerUI.CreateToolbarButton("Save Settings", () => AudioService.SetSettings(), DebuggerUI.EButtonStyle.Positive));
            settingsCard.Add(settingsRow);
        }

        #endregion

        #region 私有 [PRIVATE]

        private void AddMasterControls(VisualElement card)
        {
            AddMasterVolumeRow(card);

            VisualElement muteRow = DebuggerUI.CreateToolbarRow();
            muteRow.style.marginRight = 0f;
            VisualElement muteToggle = DebuggerUI.CreateToggle("Mute", AudioService.MasterMute, value => AudioService.MasterMute = value);
            muteToggle.style.minHeight = 26f;
            muteRow.Add(muteToggle);
            card.Add(muteRow);

            VisualElement actionRow = DebuggerUI.CreateToolbarRow();
            actionRow.Add(DebuggerUI.CreateToolbarButton("Pause All", () => AudioService.PauseAll()));
            actionRow.Add(DebuggerUI.CreateToolbarButton("Unpause All", () => AudioService.UnpauseAll()));
            actionRow.Add(DebuggerUI.CreateToolbarButton("Stop All", () => AudioService.StopAll(), DebuggerUI.EButtonStyle.Danger));
            card.Add(actionRow);
        }

        private static void AddTrackControls(VisualElement card, EAudioTrack track)
        {
            AddTrackVolumeRow(card, track.ToString(),
                () => AudioService.GetTrackVolume(track),
                value => AudioService.SetTrackVolume(track, value));

            VisualElement actionRow = DebuggerUI.CreateToolbarRow();
            actionRow.style.marginRight = 0f;

            VisualElement muteToggle = DebuggerUI.CreateToggle("Mute", AudioService.GetTrackMute(track), value => AudioService.SetTrackMute(track, value));
            muteToggle.style.minHeight = 26f;
            actionRow.Add(muteToggle);

            actionRow.Add(DebuggerUI.CreateToolbarButton("Pause", () => AudioService.PauseTrack(track)));
            actionRow.Add(DebuggerUI.CreateToolbarButton("Unpause", () => AudioService.UnpauseTrack(track)));
            actionRow.Add(DebuggerUI.CreateToolbarButton("Stop", () => AudioService.StopTrack(track), DebuggerUI.EButtonStyle.Danger));
            card.Add(actionRow);
        }

        private static VisualElement CreateVolumeRow(string label)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-slider-row");
            row.AddToClassList("dbg-slider-row--narrow");

            Label titleLabel = new Label(label);
            titleLabel.AddToClassList("dbg-slider-row__title");
            row.Add(titleLabel);
            return row;
        }

        private static void AddMasterVolumeRow(VisualElement card)
        {
            VisualElement row = CreateVolumeRow("Master");

            Label valueLabel = new Label(StringUtility.Format("{0:P0}", AudioService.MasterVolume));
            valueLabel.AddToClassList("dbg-slider-value");

            Slider slider = DebuggerUI.CreateSlider(0f, 1f, AudioService.MasterVolume, value =>
            {
                AudioService.MasterVolume = value;
                valueLabel.text = StringUtility.Format("{0:P0}", value);
            });
            slider.RegisterValueChangedCallback(evt => valueLabel.text = StringUtility.Format("{0:P0}", evt.newValue));

            row.Add(slider);
            row.Add(valueLabel);
            card.Add(row);
        }

        /// <summary>
        /// 音轨音量行：SliderInt 0-100%（步进 1%），映射到 0~<see cref="AudioGroupConfig.MAXIMAL_VOLUME"/>。
        /// </summary>
        private static void AddTrackVolumeRow(VisualElement card, string label, Func<float> getter, Action<float> setter)
        {
            VisualElement row = CreateVolumeRow(label);

            Label valueLabel = new Label(FormatPercent(VolumeToPercent(getter())));
            valueLabel.AddToClassList("dbg-slider-value");

            SliderInt slider = new SliderInt(0, 100)
            {
                value = VolumeToPercent(getter())
            };
            slider.style.flexGrow = 1f;
            slider.style.minHeight = 24f;
            slider.RegisterValueChangedCallback(evt =>
            {
                setter(PercentToVolume(evt.newValue));
                valueLabel.text = FormatPercent(evt.newValue);
            });

            row.Add(slider);
            row.Add(valueLabel);
            card.Add(row);
        }

        private static int VolumeToPercent(float volume)
        {
            const float max = AudioGroupConfig.MAXIMAL_VOLUME;
            return Mathf.Clamp(Mathf.RoundToInt(volume / max * 100f), 0, 100);
        }

        private static float PercentToVolume(int percent)
        {
            return percent / 100f * AudioGroupConfig.MAXIMAL_VOLUME;
        }

        private static string FormatPercent(int percent) => StringUtility.Format("{0}%", percent);

        #endregion
    }
}
