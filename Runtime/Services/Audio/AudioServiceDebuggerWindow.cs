using System;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频服务调试视图（原生 UI Toolkit，经 <see cref="AudioService.OnInit"/> 注册进游戏内调试器 "Profiler/Audio"）。
    /// <para>提供主音量与各音轨（枚举全量生成）音量/静音实时控制。</para>
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
            AddVolumeSlider(masterCard, "Master", () => AudioService.MasterVolume, value => AudioService.MasterVolume = value);

            VisualElement trackCard = AddSection(root, "音轨 [TRACKS]");
            var values = (EAudioTrack[])Enum.GetValues(typeof(EAudioTrack));
            for (int i = 0; i < values.Length; i++)
            {
                AddTrackControls(trackCard, values[i]);
            }
        }

        #endregion

        #region 私有 [PRIVATE]

        private static void AddTrackControls(VisualElement card, EAudioTrack track)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-slider-row");
            row.AddToClassList("dbg-slider-row--narrow");

            Label titleLabel = new Label(track.ToString());

            Slider slider = DebuggerUI.CreateSlider(0f, 1f, AudioService.GetTrackVolume(track),
                value => AudioService.SetTrackVolume(track, value));

            Label valueLabel = new Label();
            valueLabel.AddToClassList("dbg-slider-value");
            valueLabel.text = StringUtility.Format("{0:P0}", AudioService.GetTrackVolume(track));

            VisualElement toggleRow = DebuggerUI.CreateToolbarRow();
            toggleRow.style.marginRight = 0f;
            VisualElement muteToggle = DebuggerUI.CreateToggle("Mute", AudioService.GetTrackMute(track), value => AudioService.SetTrackMute(track, value));
            muteToggle.style.minHeight = 26f;
            toggleRow.Add(muteToggle);

            slider.RegisterValueChangedCallback(evt => valueLabel.text = StringUtility.Format("{0:P0}", evt.newValue));

            row.Add(titleLabel);
            row.Add(slider);
            row.Add(valueLabel);
            row.Add(toggleRow);
            card.Add(row);
        }

        private void AddVolumeSlider(VisualElement card, string label, System.Func<float> getter, System.Action<float> setter)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("dbg-slider-row");
            row.AddToClassList("dbg-slider-row--narrow");

            UnityEngine.UIElements.Label titleLabel = new UnityEngine.UIElements.Label(label);

            Slider slider = DebuggerUI.CreateSlider(0f, 1f, getter(), value =>
            {
                setter(value);
                if (_masterVolumeLabel != null)
                {
                    _masterVolumeLabel.text = StringUtility.Format("{0:P0}", value);
                }
            });

            _masterVolumeLabel = new UnityEngine.UIElements.Label();
            _masterVolumeLabel.AddToClassList("dbg-slider-value");
            _masterVolumeLabel.text = StringUtility.Format("{0:P0}", getter());

            row.Add(titleLabel);
            row.Add(slider);
            row.Add(_masterVolumeLabel);
            card.Add(row);
        }

        #endregion

        #region 私有字段 [PRIVATE FIELDS]

        // 实例字段：跨窗口重建/域重载后 static 引用会向已脱离的可视树写值
        private UnityEngine.UIElements.Label _masterVolumeLabel;

        #endregion
    }
}
