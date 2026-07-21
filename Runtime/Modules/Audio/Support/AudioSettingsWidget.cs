using Moirai.Atropos.Schedulers;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音量设置绑定
    /// </summary>
    public class AudioSettingsWidget : MonoBehaviour
    {
        [Tooltip("切换组件的 On 代表静音，反之 Off 代表静音")]
        [SerializeField] private bool m_ToggleOnIsMute = true;

        [Header("主音量")]
        [SerializeField] private Slider m_MasterSlider;
        [SerializeField] private TMP_Text m_MasterSliderValue;
        [Tooltip("静音按钮")]
        [SerializeField] private Toggle m_MasterMuteToggle;
        private float _masterVolume;
        private bool _masterMute;

        [Header("音乐音量")]
        [SerializeField] private Slider m_MusicSlider;
        [SerializeField] private TMP_Text m_MusicSliderValue;
        [Tooltip("静音按钮")]
        [SerializeField] private Toggle m_MusicMuteToggle;
        private float _musicVolume;
        private bool _musicMute;

        [Header("音效音量")]
        [SerializeField] private Slider m_SfxSlider;
        [SerializeField] private TMP_Text m_SfxSliderValue;
        [Tooltip("静音按钮")]
        [SerializeField] private Toggle m_SfxMuteToggle;
        private float _sfxVolume;
        private bool _sfxMute;

        [Header("UI音量")]
        [SerializeField] private Slider m_UISlider;
        [SerializeField] private TMP_Text m_UISliderValue;
        [Tooltip("静音按钮")]
        [SerializeField] private Toggle m_UIMuteToggle;
        private float _uiVolume;
        private bool _uiMute;

        [Header("人声音量")]
        [SerializeField] private Slider m_VoiceSlider;
        [SerializeField] private TMP_Text m_VoiceSliderValue;
        [Tooltip("静音按钮")]
        [SerializeField] private Toggle m_VoiceMuteToggle;
        private float _voiceVolume;
        private bool _voiceMute;

        private const float MULTIPLE = 100f;

        public delegate void OnSettingChangeDelegate(bool hasChanged);
        /// <summary>
        /// 音量设置有修改时触发的事件
        /// </summary>
        public OnSettingChangeDelegate onSettingChanged;

        private void SettingChange()
        {
            if (GameModule.Audio == null) return;

            bool hasChanged = false;
            hasChanged |= _masterVolume != GameModule.Audio.MasterVolume;
            hasChanged |= _masterMute != GameModule.Audio.MasterMute;
            hasChanged |= _musicVolume != GameModule.Audio.GetTrackVolume(AudioTrack.Music);
            hasChanged |= _musicMute != GameModule.Audio.GetTrackMute(AudioTrack.Music);
            hasChanged |= _sfxVolume != GameModule.Audio.GetTrackVolume(AudioTrack.Sfx);
            hasChanged |= _sfxMute != GameModule.Audio.GetTrackMute(AudioTrack.Sfx);
            hasChanged |= _uiVolume != GameModule.Audio.GetTrackVolume(AudioTrack.UI);
            hasChanged |= _uiMute != GameModule.Audio.GetTrackMute(AudioTrack.UI);
            hasChanged |= _voiceVolume != GameModule.Audio.GetTrackVolume(AudioTrack.Voice);
            hasChanged |= _voiceMute != GameModule.Audio.GetTrackMute(AudioTrack.Voice);

            onSettingChanged?.Invoke(hasChanged);
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
                m_MasterSlider.onValueChanged.AddListener(SetMasterVolume);
            }
            m_MasterMuteToggle?.onValueChanged.AddListener(ToggleMasterMute);

            if (m_MusicSlider != null)
            {
                m_MusicSlider.wholeNumbers = true;
                m_MusicSlider.onValueChanged.AddListener(SetMusicVolume);
            }
            m_MusicMuteToggle?.onValueChanged.AddListener(ToggleMusicMute);

            if (m_SfxSlider != null)
            {
                m_SfxSlider.wholeNumbers = true;
                m_SfxSlider.onValueChanged.AddListener(SetSfxVolume);
            }
            m_SfxMuteToggle?.onValueChanged.AddListener(ToggleSfxMute);

            if (m_UISlider != null)
            {
                m_UISlider.wholeNumbers = true;
                m_UISlider.onValueChanged.AddListener(SetUIVolume);
            }
            m_UIMuteToggle?.onValueChanged.AddListener(ToggleUIMute);

            if (m_VoiceSlider != null)
            {
                m_VoiceSlider.wholeNumbers = true;
                m_VoiceSlider.onValueChanged.AddListener(SetVoiceVolume);
            }
            m_VoiceMuteToggle?.onValueChanged.AddListener(ToggleVoiceMute);

            // EventManager.RegisterCallback<AudioModuleEvent>(OnAudioModuleEvent);
            // EventManager.RegisterCallback<AudioTrackEvent>(OnAudioTrackEvent);
            // EventManager.RegisterCallback<AudioTrackFadeEvent>(OnAudioTrackFadeEvent);
        }

        private void OnDestroy()
        {
            m_MasterSlider?.onValueChanged.RemoveListener(SetMasterVolume);
            m_MasterMuteToggle?.onValueChanged.RemoveListener(ToggleMasterMute);

            m_MusicSlider?.onValueChanged.RemoveListener(SetMusicVolume);
            m_MusicMuteToggle?.onValueChanged.RemoveListener(ToggleMusicMute);

            m_SfxSlider?.onValueChanged.RemoveListener(SetSfxVolume);
            m_SfxMuteToggle?.onValueChanged.RemoveListener(ToggleSfxMute);

            m_UISlider?.onValueChanged.RemoveListener(SetUIVolume);
            m_UIMuteToggle?.onValueChanged.RemoveListener(ToggleUIMute);

            m_VoiceSlider?.onValueChanged.RemoveListener(SetVoiceVolume);
            m_VoiceMuteToggle?.onValueChanged.RemoveListener(ToggleVoiceMute);

            // EventManager.UnregisterCallback<AudioModuleEvent>(OnAudioModuleEvent);
            // EventManager.UnregisterCallback<AudioTrackEvent>(OnAudioTrackEvent);
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

            if (m_MusicSlider != null)
            {
                m_MusicSlider.minValue = 0f;
                m_MusicSlider.maxValue = MULTIPLE;
            }

            if (m_SfxSlider != null)
            {
                m_SfxSlider.minValue = 0f;
                m_SfxSlider.maxValue = MULTIPLE;
            }

            if (m_UISlider != null)
            {
                m_UISlider.minValue = 0f;
                m_UISlider.maxValue = MULTIPLE;
            }

            if (m_VoiceSlider != null)
            {
                m_VoiceSlider.minValue = 0f;
                m_VoiceSlider.maxValue = MULTIPLE;
            }

            // 获取初始值
            InitSet();

            // 更新组件值
            UpdateComponentsValue();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 重置初始值
        /// </summary>
        private void InitSet()
        {
            // 获取初始值
            _masterVolume = GameModule.Audio.MasterVolume;
            _masterMute = GameModule.Audio.MasterMute;
            _musicVolume = GameModule.Audio.GetTrackVolume(AudioTrack.Music);
            _musicMute = GameModule.Audio.GetTrackMute(AudioTrack.Music);
            _sfxVolume = GameModule.Audio.GetTrackVolume(AudioTrack.Sfx);
            _sfxMute = GameModule.Audio.GetTrackMute(AudioTrack.Sfx);
            _uiVolume = GameModule.Audio.GetTrackVolume(AudioTrack.UI);
            _uiMute = GameModule.Audio.GetTrackMute(AudioTrack.UI);
            _voiceVolume = GameModule.Audio.GetTrackVolume(AudioTrack.Voice);
            _voiceMute = GameModule.Audio.GetTrackMute(AudioTrack.Voice);
        }

        /// <summary>
        /// Write，设置主音量
        /// </summary>
        /// <param name="newVolume"></param>
        private void SetMasterVolume(float newVolume)
        {
            if (m_MasterSliderValue != null)
            {
                m_MasterSliderValue.text = newVolume.ToString("f0");
            }

            if (GameModule.Audio != null) GameModule.Audio.MasterVolume = MathsUtility.Remap(newVolume, 0f, MULTIPLE, 0f, 1f);
            SettingChange();
        }

        /// <summary>
        /// Write，设置主音量静音
        /// </summary>
        /// <param name="state"></param>
        private void ToggleMasterMute(bool state)
        {
            bool isMute = m_ToggleOnIsMute ? state : !state;

            if (m_MasterSlider != null)
            {
                m_MasterSlider.interactable = !isMute;
            }

            if (GameModule.Audio != null) GameModule.Audio.MasterMute = isMute;
            // Debug.Log($"MasterMute {isMute}({_masterMute})");
            SettingChange();
        }

        private void SetSfxVolume(float newVolume) => SetTrackVolume(AudioTrack.Sfx, newVolume);
        private void ToggleSfxMute(bool state) => ToggleTrackMute(AudioTrack.Sfx, state);

        private void SetMusicVolume(float newVolume) => SetTrackVolume(AudioTrack.Music, newVolume);
        private void ToggleMusicMute(bool state) => ToggleTrackMute(AudioTrack.Music, state);

        private void SetUIVolume(float newVolume) => SetTrackVolume(AudioTrack.UI, newVolume);
        private void ToggleUIMute(bool state) => ToggleTrackMute(AudioTrack.UI, state);

        private void SetVoiceVolume(float newVolume) => SetTrackVolume(AudioTrack.Voice, newVolume);
        private void ToggleVoiceMute(bool state) => ToggleTrackMute(AudioTrack.Voice, state);

        /// <summary>
        /// Write，设置音轨的音量
        /// </summary>
        /// <param name="track"></param>
        /// <param name="newVolume"></param>
        private void SetTrackVolume(AudioTrack track, float newVolume)
        {
            switch (track)
            {
                case AudioTrack.Sfx:
                    if (m_SfxSliderValue != null)
                    {
                        m_SfxSliderValue.text = newVolume.ToString("f0");
                    }
                    break;
                case AudioTrack.UI:
                    if (m_UISliderValue != null)
                    {
                        m_UISliderValue.text = newVolume.ToString("f0");
                    }
                    break;
                case AudioTrack.Music:
                    if (m_MusicSliderValue != null)
                    {
                        m_MusicSliderValue.text = newVolume.ToString("f0");
                    }
                    break;
                case AudioTrack.Voice:
                    if (m_VoiceSliderValue != null)
                    {
                        m_VoiceSliderValue.text = newVolume.ToString("f0");
                    }
                    break;
            }

            if (GameModule.Audio != null) GameModule.Audio.SetTrackVolume(track, MathsUtility.Remap(newVolume, 0f, MULTIPLE, 0f, 1f));
            SettingChange();
        }

        private void ToggleTrackMute(AudioTrack track, bool state)
        {
            bool isMute = m_ToggleOnIsMute ? state : !state;

            switch (track)
            {
                case AudioTrack.Sfx:
                    if (m_SfxSlider != null)
                    {
                        m_SfxSlider.interactable = !isMute;
                    }
                    break;
                case AudioTrack.UI:
                    if (m_UISlider != null)
                    {
                        m_UISlider.interactable = !isMute;
                    }
                    break;
                case AudioTrack.Music:
                    if (m_MusicSlider != null)
                    {
                        m_MusicSlider.interactable = !isMute;
                    }
                    break;
                case AudioTrack.Voice:
                    if (m_VoiceSlider != null)
                    {
                        m_VoiceSlider.interactable = !isMute;
                    }
                    break;
            }

            if (GameModule.Audio != null) GameModule.Audio.SetTrackMute(track, isMute);
            SettingChange();
        }

        /// <summary>
        /// Read，更新滑动条、切换组件的值
        /// </summary>
        private void UpdateComponentsValue()
        {
            bool masterMute = (GameModule.Audio != null) && GameModule.Audio.MasterMute;
            if (m_MasterSlider != null)
            {
                m_MasterSlider.value = (GameModule.Audio != null) ? MathsUtility.Remap(GameModule.Audio.MasterVolume, 0f, 1f, 0f, MULTIPLE) : 1f;
                m_MasterSlider.interactable = !masterMute;
            }
            if (m_MasterSliderValue != null)
            {
                m_MasterSliderValue.text = m_MasterSlider.value.ToString("f0");
            }
            if (m_MasterMuteToggle != null)
            {
                m_MasterMuteToggle.isOn = m_ToggleOnIsMute ? masterMute : !masterMute;
            }

            bool musicMute = (GameModule.Audio != null) && GameModule.Audio.GetTrackMute(AudioTrack.Music);
            if (m_MusicSlider != null)
            {
                m_MusicSlider.value = (GameModule.Audio != null) ? MathsUtility.Remap(GameModule.Audio.GetTrackVolume(AudioTrack.Music), 0f, 1f, 0f, MULTIPLE) : 1f;
                m_MusicSlider.interactable = !musicMute;
            }
            if (m_MusicSliderValue != null)
            {
                m_MusicSliderValue.text = m_MusicSlider.value.ToString("f0");
            }
            if (m_MusicMuteToggle != null)
            {
                m_MusicMuteToggle.isOn = m_ToggleOnIsMute ? musicMute : !musicMute;
            }

            bool sfxMute = (GameModule.Audio != null) && GameModule.Audio.GetTrackMute(AudioTrack.Sfx);
            if (m_SfxSlider != null)
            {
                m_SfxSlider.value = (GameModule.Audio != null) ? MathsUtility.Remap(GameModule.Audio.GetTrackVolume(AudioTrack.Sfx), 0f, 1f, 0f, MULTIPLE) : 1f;
                m_SfxSlider.interactable = !sfxMute;
            }
            if (m_SfxSliderValue != null)
            {
                m_SfxSliderValue.text = m_SfxSlider.value.ToString("f0");
            }
            if (m_SfxMuteToggle != null)
            {
                m_SfxMuteToggle.isOn = m_ToggleOnIsMute ? sfxMute : !sfxMute;
            }

            bool uiMute = (GameModule.Audio != null) && GameModule.Audio.GetTrackMute(AudioTrack.UI);
            if (m_UISlider != null)
            {
                m_UISlider.value = (GameModule.Audio != null) ? MathsUtility.Remap(GameModule.Audio.GetTrackVolume(AudioTrack.UI), 0f, 1f, 0f, MULTIPLE) : 1f;
                m_UISlider.interactable = !uiMute;
            }
            if (m_UISliderValue != null)
            {
                m_UISliderValue.text = m_UISlider.value.ToString("f0");
            }
            if (m_UIMuteToggle != null)
            {
                m_UIMuteToggle.isOn = m_ToggleOnIsMute ? uiMute : !uiMute;
            }

            bool voiceMute = (GameModule.Audio != null) && GameModule.Audio.GetTrackMute(AudioTrack.Voice);
            if (m_VoiceSlider != null)
            {
                m_VoiceSlider.value = (GameModule.Audio != null) ? MathsUtility.Remap(GameModule.Audio.GetTrackVolume(AudioTrack.Voice), 0f, 1f, 0f, MULTIPLE) : 1f;
                m_VoiceSlider.interactable = !voiceMute;
            }
            if (m_VoiceSliderValue != null)
            {
                m_VoiceSliderValue.text = m_VoiceSlider.value.ToString("f0");
            }
            if (m_VoiceMuteToggle != null)
            {
                m_VoiceMuteToggle.isOn = m_ToggleOnIsMute ? voiceMute : !voiceMute;
            }
        }

        private void OnAudioModuleEvent(AudioModuleEvent evt)
        {
            UpdateComponentsValue();
        }
        
        private void OnAudioTrackEvent(AudioTrackEvent evt)
        {
            if (evt.IsMaster)
            {
                switch (evt.TrackEventType)
                {
                    case AudioTrackEvent.EAudioTrackEventType.MuteTrack:
                        ToggleMasterMute(false);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.UnmuteTrack:
                        ToggleMasterMute(true);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.SetTrackVolume:
                        SetMasterVolume(evt.Volume);
                        break;
                }
            }
            else
            {
                switch (evt.TrackEventType)
                {
                    case AudioTrackEvent.EAudioTrackEventType.MuteTrack:
                        ToggleTrackMute(evt.Track, false);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.UnmuteTrack:
                        ToggleTrackMute(evt.Track, true);
                        break;
                    case AudioTrackEvent.EAudioTrackEventType.SetTrackVolume:
                        SetTrackVolume(evt.Track, evt.Volume);
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