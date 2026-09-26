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
                root.Add(DebuggerUI.CreateHintLabel("Audio service not ready (enter Play Mode and finish initialization)."));
                return;
            }

            VisualElement masterCard = AddSection(root, "MASTER VOLUME");
            AddMasterControls(masterCard);

            VisualElement trackCard = AddSection(root, "TRACKS");
            var values = (EAudioTrack[])Enum.GetValues(typeof(EAudioTrack));
            for (int i = 0; i < values.Length; i++)
            {
                AddTrackControls(trackCard, values[i]);
            }

            VisualElement cacheCard = AddSection(root, "CLIP CACHE");
            AddCacheControls(cacheCard);

            VisualElement settingsCard = AddSection(root, "SETTINGS");
            VisualElement settingsRow = DebuggerUI.CreateToolbarRow();
            settingsRow.Add(DebuggerUI.CreateToolbarButton("Save Settings", () => AudioService.SetSettings(), DebuggerUI.EButtonStyle.Positive));
            settingsCard.Add(settingsRow);
        }

        #endregion

        #region 私有 [PRIVATE]

        /// <summary>
        /// Clip 缓存与 Ducking 概览。这些计数此前没有任何运行期读者，而线上"音效没出来"的第一嫌疑
        /// 恰恰是缓存满载判负、地址在失败冷却、或快照没绑上——不显示就等于看不到。
        /// </summary>
        private static void AddCacheControls(VisualElement card)
        {
            AudioClipCache cache = AudioService.ClipCacheForDiagnostics;
            if (cache == null)
            {
                card.Add(DebuggerUI.CreateHintLabel("Current backend holds no clip leases (middleware plays through the event path)."));
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("Mix snapshot {0}", AudioMixService.Current)));
                return;
            }

            card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format(
                "Entries {0}/{1} · Loading {2} · Pinned {3} · Failed cooldown {4} · TTL {5:0.#}s · Default policy {6}",
                cache.Count, cache.Capacity, cache.LoadingCount, cache.PinnedCount,
                cache.FailedAddressCount, cache.Ttl, cache.DefaultPolicy)));
            card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format(
                "Pooled visible {0} · Mix snapshot {1} · Ducking {2}",
                cache.PoolReadOnly.Count, AudioMixService.Current,
                AudioVoiceDucking.IsDucking ? "Dialogue taken" : "Not taken")));

            const int MaxEntryRows = 8;
            int shown = 0;
            for (var entry = cache.FirstEntry; entry != null && shown < MaxEntryRows; entry = entry.AllNext)
            {
                float idle = Time.realtimeSinceStartup - entry.LastUseTime;
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("  {0}  ref {1} · {2} · {3} · {4:0.0}s ago",
                    entry.Address, entry.RefCount, entry.CachePolicy,
                    entry.Loading ? "Loading" : (entry.IsLoaded ? "Loaded" : "Empty"), idle)));
                shown++;
            }

            if (cache.Count > MaxEntryRows)
            {
                card.Add(DebuggerUI.CreateHintLabel(StringUtility.Format("  …{0} more not shown", cache.Count - MaxEntryRows)));
            }

            VisualElement row = DebuggerUI.CreateToolbarRow();
            row.Add(DebuggerUI.CreateToolbarButton("Clear Cache (Keep Playing)", () => AudioService.ClearClipCache(false)));
            row.Add(DebuggerUI.CreateToolbarButton("Force Clear", () => AudioService.ClearClipCache(true), DebuggerUI.EButtonStyle.Danger));
            card.Add(row);
        }

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
