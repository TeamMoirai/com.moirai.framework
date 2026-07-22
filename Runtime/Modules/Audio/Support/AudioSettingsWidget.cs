using System;
using Moirai.Atropos.Schedulers;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音量设置绑定，按 <see cref="EAudioTrack"/> 动态适配。
    /// </summary>
    public class AudioSettingsWidget : MonoBehaviour
    {
        [Serializable]
        private struct TrackWidgets
        {
            [Tooltip("目标音轨")]
            [SerializeField] private EAudioTrack m_TargetTrack;
            public EAudioTrack TargetTrack => m_TargetTrack;
            [Tooltip("音量滑条")]
            [SerializeField] private Slider m_Slider;
            public Slider Slider => m_Slider;
            [Tooltip("音量数值文本")]
            [SerializeField] private TMP_Text m_SliderValue;
            public TMP_Text SliderValue => m_SliderValue;
            [Tooltip("静音按钮")]
            [SerializeField] private Toggle m_MuteToggle;
            public Toggle MuteToggle => m_MuteToggle;
        }

        [Tooltip("切换组件的 On 代表静音，反之 Off 代表静音")]
        [SerializeField] private bool m_ToggleOnIsMute = true;

        [Header("主音量")]
        [SerializeField] private Slider m_MasterSlider;
        [SerializeField] private TMP_Text m_MasterSliderValue;
        [Tooltip("静音按钮")]
        [SerializeField] private Toggle m_MasterMuteToggle;

        [Header("各音轨 UI（通过 m_TargetTrack 映射，顺序随意）")]
        [SerializeField] private TrackWidgets[] m_TrackWidgets;

        private const float MULTIPLE = 100f;
        private static readonly EAudioTrack[] s_TrackValues = (EAudioTrack[])Enum.GetValues(typeof(EAudioTrack));

        private float[] _trackVolumes;
        private bool[] _trackMutes;
        private float _masterVolume;
        private bool _masterMute;

        /// <summary>音量设置有修改时触发的事件</summary>
        /// <returns><see cref="bool"/> hasChanged</returns>
        public Action<bool> onSettingChanged;

        private TrackWidgets[] _trackLookup;
        /// <summary>运行时查找表，按 (int)EAudioTrack 索引 </summary>
        private TrackWidgets[] TrackLookup
        {
            get
            {
                if (_trackLookup != null) return _trackLookup;

                _trackLookup = new TrackWidgets[s_TrackValues.Length];
                if (m_TrackWidgets != null)
                {
                    for (int i = 0; i < m_TrackWidgets.Length; i++)
                    {
                        int idx = (int)m_TrackWidgets[i].TargetTrack;
                        if (idx >= 0 && idx < _trackLookup.Length)
                            _trackLookup[idx] = m_TrackWidgets[i];
                    }
                }

                return _trackLookup;
            }
        }

        private void EnsureTrackState()
        {
            int count = TrackLookup.Length;
            if (_trackVolumes == null || _trackVolumes.Length != count)
                _trackVolumes = new float[count];
            if (_trackMutes == null || _trackMutes.Length != count)
                _trackMutes = new bool[count];
        }

        #region 测试方法 [TEST METHODS]

        [Button]
        private void SaveAudioSettings()
        {
            AudioModuleEvent.Trigger(AudioModuleEvent.EAudioModuleEventType.SetSettings);
        }

        [Button]
        private void LoadAudioSettings()
        {
            AudioModuleEvent.Trigger(AudioModuleEvent.EAudioModuleEventType.LoadSettings);
        }

        [Button]
        private void ResetAudioSettings()
        {
            AudioModuleEvent.Trigger(AudioModuleEvent.EAudioModuleEventType.ResetSettings);
        }

        #endregion

        #region 引擎方法 [UNITY METHODS]

        private void Awake()
        {
            if (m_MasterSlider != null)
            {
                m_MasterSlider.wholeNumbers = true;
                m_MasterSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }
            m_MasterMuteToggle?.onValueChanged.AddListener(OnMasterMuteChanged);

            var tracks = TrackLookup;
            for (int i = 0; i < tracks.Length; i++)
            {
                var t = tracks[i];
                if (t.Slider != null)
                {
                    t.Slider.wholeNumbers = true;
                    int trackIndex = (int)t.TargetTrack;
                    t.Slider.onValueChanged.AddListener(v => OnTrackVolumeChanged(trackIndex, v));
                }
                if (t.MuteToggle != null)
                {
                    int trackIndex = (int)t.TargetTrack;
                    t.MuteToggle.onValueChanged.AddListener(s => OnTrackMuteChanged(trackIndex, s));
                }
            }

            // EventManager.RegisterCallback<AudioModuleEvent>(OnAudioModuleEvent);
            // EventManager.RegisterCallback<AudioTrackControlEvent>(OnAudioTrackEvent);
            // EventManager.RegisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
        }

        private void OnDestroy()
        {
            m_MasterSlider?.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            m_MasterMuteToggle?.onValueChanged.RemoveListener(OnMasterMuteChanged);
            // 音轨 lambda 带捕获无法精确移除，OnDestroy 时对象即将销毁，无需清理

            // EventManager.UnregisterCallback<AudioModuleEvent>(OnAudioModuleEvent);
            // EventManager.UnregisterCallback<AudioTrackControlEvent>(OnAudioTrackEvent);
            // EventManager.UnregisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
        }

        private void OnEnable()
        {
            if (GameModule.Audio == null)
            {
                Log.Error($"{nameof(AudioModule)} is null");
                return;
            }

            if (m_MasterSlider != null)
            {
                m_MasterSlider.minValue = 0f;
                m_MasterSlider.maxValue = MULTIPLE;
            }

            var tracks = TrackLookup;
            for (int i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Slider != null)
                {
                    tracks[i].Slider.minValue = 0f;
                    tracks[i].Slider.maxValue = MULTIPLE;
                }
            }

            // 获取初始值
            InitSet();

            // 更新组件值
            UpdateComponentsValue();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void InitSet()
        {
            EnsureTrackState();

            _masterVolume = GameModule.Audio.MasterVolume;
            _masterMute = GameModule.Audio.MasterMute;

            var values = s_TrackValues;
            for (int i = 0; i < values.Length; i++)
            {
                _trackVolumes[i] = GameModule.Audio.GetTrackVolume(values[i]);
                _trackMutes[i] = GameModule.Audio.GetTrackMute(values[i]);
            }
        }

        // ===== 主音量 =====

        private void OnMasterVolumeChanged(float newVolume)
        {
            if (m_MasterSliderValue != null)
                m_MasterSliderValue.text = newVolume.ToString("f0");

            if (GameModule.Audio != null)
                GameModule.Audio.MasterVolume = MathsUtility.Remap(newVolume, 0f, MULTIPLE, 0f, 1f);
            HandleSettingChange();
        }

        private void OnMasterMuteChanged(bool state)
        {
            bool isMute = m_ToggleOnIsMute ? state : !state;

            if (m_MasterSlider != null)
                m_MasterSlider.interactable = !isMute;

            if (GameModule.Audio != null)
                GameModule.Audio.MasterMute = isMute;
            HandleSettingChange();
        }

        // ===== 音轨音量 / 静音 =====

        private void OnTrackVolumeChanged(int index, float newVolume)
        {
            if (index < 0 || index >= TrackLookup.Length) return;

            var widget = TrackLookup[index];
            if (widget.SliderValue != null)
                widget.SliderValue.text = newVolume.ToString("f0");

            EAudioTrack track = (EAudioTrack)index;
            GameModule.Audio?.SetTrackVolume(track, MathsUtility.Remap(newVolume, 0f, MULTIPLE, 0f, 1f));
            HandleSettingChange(track);
        }

        private void OnTrackMuteChanged(int index, bool state)
        {
            if (index < 0 || index >= TrackLookup.Length) return;

            bool isMute = m_ToggleOnIsMute ? state : !state;
            var widget = TrackLookup[index];

            if (widget.Slider != null)
                widget.Slider.interactable = !isMute;

            EAudioTrack track = (EAudioTrack)index;
            GameModule.Audio?.SetTrackMute(track, isMute);
            HandleSettingChange(track);
        }

        /// <summary>
        /// 检测快照与 AudioModule 当前状态是否一致。
        /// </summary>
        /// <param name="changedTrack">用户刚操作的音轨，仅检测该音轨；null 表示检测 master。</param>
        private void HandleSettingChange(EAudioTrack? changedTrack = null)
        {
            if (GameModule.Audio == null) return;

            bool hasChanged;
            if (changedTrack.HasValue)
            {
                int i = (int)changedTrack.Value;
                EnsureTrackState();
                hasChanged = _trackVolumes[i] != GameModule.Audio.GetTrackVolume(changedTrack.Value)
                          || _trackMutes[i] != GameModule.Audio.GetTrackMute(changedTrack.Value);
            }
            else
            {
                hasChanged = _masterVolume != GameModule.Audio.MasterVolume
                          || _masterMute != GameModule.Audio.MasterMute;
            }

            onSettingChanged?.Invoke(hasChanged);
        }

        /// <summary>
        /// Read，更新滑动条、切换组件的值
        /// </summary>
        private void UpdateComponentsValue()
        {
            EnsureTrackState();

            // 主音量
            bool masterMute = (GameModule.Audio != null) && GameModule.Audio.MasterMute;
            if (m_MasterSlider != null)
            {
                m_MasterSlider.value = (GameModule.Audio != null)
                    ? MathsUtility.Remap(GameModule.Audio.MasterVolume, 0f, 1f, 0f, MULTIPLE)
                    : 1f;
                m_MasterSlider.interactable = !masterMute;
            }
            if (m_MasterSliderValue != null)
                m_MasterSliderValue.text = m_MasterSlider.value.ToString("f0");
            if (m_MasterMuteToggle != null)
                m_MasterMuteToggle.isOn = m_ToggleOnIsMute ? masterMute : !masterMute;

            // 各音轨
            var tracks = TrackLookup;
            var values = s_TrackValues;
            for (int i = 0; i < values.Length; i++)
            {
                var widget = tracks[i];
                bool mute = (GameModule.Audio != null) && GameModule.Audio.GetTrackMute(values[i]);
                if (widget.Slider != null)
                {
                    widget.Slider.value = (GameModule.Audio != null)
                        ? MathsUtility.Remap(GameModule.Audio.GetTrackVolume(values[i]), 0f, 1f, 0f, MULTIPLE)
                        : 1f;
                    widget.Slider.interactable = !mute;
                }
                if (widget.SliderValue != null)
                    widget.SliderValue.text = widget.Slider.value.ToString("f0");
                if (widget.MuteToggle != null)
                    widget.MuteToggle.isOn = m_ToggleOnIsMute ? mute : !mute;
            }
        }

        private void OnAudioModuleEvent(AudioModuleEvent evt)
        {
            UpdateComponentsValue();
        }

        private void OnAudioTrackEvent(AudioTrackControlEvent evt)
        {
            if (evt.IsMaster)
            {
                switch (evt.TrackEventType)
                {
                    case AudioTrackControlEvent.EAudioTrackEventType.MuteTrack:
                        OnMasterMuteChanged(false);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.UnmuteTrack:
                        OnMasterMuteChanged(true);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.SetTrackVolume:
                        OnMasterVolumeChanged(evt.Volume);
                        break;
                }
            }
            else
            {
                int index = (int)evt.Track;
                switch (evt.TrackEventType)
                {
                    case AudioTrackControlEvent.EAudioTrackEventType.MuteTrack:
                        OnTrackMuteChanged(index, false);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.UnmuteTrack:
                        OnTrackMuteChanged(index, true);
                        break;
                    case AudioTrackControlEvent.EAudioTrackEventType.SetTrackVolume:
                        OnTrackVolumeChanged(index, evt.Volume);
                        break;
                }
            }
        }

        private void OnAudioTrackFadeEvent(AudioTrackFadeEvent evt)
        {
            // 延迟更新滑动条的值
            Scheduler.Delay(evt.FadeDuration + 0.5f, UpdateComponentsValue);
        }

        #endregion
    }
}
