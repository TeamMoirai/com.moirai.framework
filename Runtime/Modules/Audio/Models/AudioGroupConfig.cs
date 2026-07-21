using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频轨道组配置。
    /// </summary>
    [Serializable]
    public sealed class AudioGroupConfig
    {
        [Tooltip("音频类型")]
        [SerializeField] private EAudioTrack m_AudioTrack;
        
        [Tooltip("所属音轨")]
        [SerializeField] private AudioMixerGroup m_AudioMixerGroup;
        
        [Tooltip("默认音量")]
        [Range(0, MAXIMAL_VOLUME)]
        [SerializeField] private float m_DefaultVolume = 1f;

        [Tooltip("将归一化音量值转换为混音器值时要的系数（分贝转换）")]
        [SerializeField] private float m_MixerValuesMultiplier = 20f;

        [Tooltip("可同时播放的最大数量")]
        [SerializeField] private int m_MaxChannel = 3;
        
        [Tooltip("否可以扩展（按需创建新的音频源）")]
        [SerializeField] private bool m_CanExpand;
        
        // 最小音量
        public const float MINIMAL_VOLUME = 0.0001f;
        // 最大音量
        public const float MAXIMAL_VOLUME = 10f;
        
        private bool _isMuted;
        private float _volume;
        
        /// <summary>
        /// 音频类型
        /// </summary>
        public EAudioTrack AudioTrack
        {
            get => m_AudioTrack;
            internal set => m_AudioTrack = value;
        }

        /// <summary>
        /// mixer 中的组对象。
        /// </summary>
        public AudioMixerGroup AudioMixerGroup
        {
            get => m_AudioMixerGroup;
            internal set => m_AudioMixerGroup = value;
        }

        /// <summary>
        /// 设置保存 -> 静音设置Key
        /// </summary>
        private string SettingConstantMute => StringUtility.Format(Constant.Setting.AUDIO_GROUP_MUTED, m_AudioTrack);
        /// <summary>
        /// 当前音轨是否静音
        /// </summary>
        public bool Mute
        {
            get => _isMuted;
            set
            {
                if (_isMuted == value) return;
                
                _isMuted = value;
                ApplyTrackVolume();
                // Log.Info($"{m_AudioTrack} Mute:{_isMuted}");
            }
        }
        
        /// <summary>
        /// 设置保存 -> 音量设置Key
        /// </summary>
        private string SettingConstantVolume => StringUtility.Format(Constant.Setting.AUDIO_GROUP_VOLUME, m_AudioTrack);
        /// <summary>
        /// 当前音轨的音量
        /// </summary>
        /// <remarks>0 ~ <see cref="MAXIMAL_VOLUME"/></remarks>
        public float Volume
        {
            get => _volume;
            set
            {
                if (Mathf.Approximately(_volume, value)) return;
                
                _volume = value;
                ApplyTrackVolume();
                // Log.Info($"{m_AudioTrack} Volume:{_volume}");
            }
        }
        
        /// <summary>
        /// 预设同时播放的最大数量
        /// </summary>
        public int MaxChannel => m_MaxChannel;
        
        /// <summary>
        /// 当没有可用的Agent时，是否可拓展
        /// </summary>
        public bool CanExpand => m_CanExpand;

        /// <summary>
        /// 写入设置
        /// </summary>
        public void SetSettings()
        {
            SettingUtility.SetBool(SettingConstantMute, _isMuted);
            SettingUtility.SetFloat(SettingConstantVolume, _volume);
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        public void LoadSettings()
        {
            _isMuted = SettingUtility.GetBool(SettingConstantMute, false);
            _volume = SettingUtility.GetFloat(SettingConstantVolume, m_DefaultVolume);
            
            ApplyTrackVolume();
            // Log.Info($"[LoadSettings] <color=orange>{m_AudioTrack.ToString()}(volume:{_volume} mute:{_isMuted})</color>");
        }
        
        /// <summary>
        /// 移除设置
        /// </summary>
        public void RemoveSetting()
        {
            SettingUtility.RemoveSetting(SettingConstantMute);
            SettingUtility.RemoveSetting(SettingConstantVolume);
            
            _isMuted = false;
            _volume = 1f;
            ApplyTrackVolume();
        }

        /// <summary>
        /// 将音量应用于所属音轨
        /// </summary>
        private void ApplyTrackVolume()
        {
            float volume = Mathf.Clamp(_isMuted ? 0f : _volume, MINIMAL_VOLUME, MAXIMAL_VOLUME);
            m_AudioMixerGroup.audioMixer.SetFloat(StringUtility.Format("{0}Volume", m_AudioMixerGroup.name), NormalizedToMixerVolume(volume));
        }
        
        /// <summary>
        /// 将归一化音量转换为混音器组 db
        /// </summary>
        /// <param name="normalizedVolume"></param>
        /// <returns></returns>
        private float NormalizedToMixerVolume(float normalizedVolume)
        {
            return Mathf.Log10(normalizedVolume) * m_MixerValuesMultiplier;
        }

        /// <summary>
        /// 将混音器音量转换为归一化值
        /// </summary>
        /// <param name="mixerVolume"></param>
        /// <returns></returns>
        private float MixerVolumeToNormalized(float mixerVolume)
        {
            return (float)Math.Pow(10, mixerVolume / m_MixerValuesMultiplier);
        }
    }
}