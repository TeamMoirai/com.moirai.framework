using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("音频设置", "音频混音器与音轨配置", -480)]
    public class AudioSettings : FrameworkSettings<AudioSettings>
    {
        [Tooltip("如果不配置 AudioGroupConfigs，则会从 AudioMixer 读取音轨配置")]
        [SerializeField] private AudioMixer m_AudioMixer;
        /// <summary>音频混音器</summary>
        public static AudioMixer AudioMixer => Instance.m_AudioMixer;

        [SerializeField] private AudioGroupConfig[] m_AudioGroupConfigs;
        /// <summary>音轨配置</summary>
        public static AudioGroupConfig[] AudioGroupConfigs => Instance.m_AudioGroupConfigs;

#if UNITY_EDITOR

        protected internal override void Reset()
        {
            // 从 Resources 中读取默认 AudioMixer
            m_AudioMixer = Resources.Load<AudioMixer>("AudioMixer");

            if (m_AudioMixer != null)
            {
                // 从传入的 audioMixer 读取音轨配置
                var audioMixerGroups = m_AudioMixer.FindMatchingGroups("Master/");
                m_AudioGroupConfigs = new AudioGroupConfig[audioMixerGroups.Length];
                for (int i = 0; i < audioMixerGroups.Length; i++)
                {
                    m_AudioGroupConfigs[i] = new AudioGroupConfig();
                    m_AudioGroupConfigs[i].AudioMixerGroup = audioMixerGroups[i];

                    Enum.TryParse<EAudioTrack>(audioMixerGroups[i].name, out var audioTrack);
                    m_AudioGroupConfigs[i].AudioTrack = audioTrack;
                }
            }
        }

#endif
    }
}