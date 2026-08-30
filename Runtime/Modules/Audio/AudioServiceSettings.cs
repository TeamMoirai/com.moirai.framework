using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    // ReSharper disable once InconsistentNaming
    [FrameworkSetting("[服务]音频设置", "音频混音器与音轨配置", -480)]
    public sealed partial class AudioServiceSettings : FrameworkSettings<AudioServiceSettings>
    {
        [InfoBox("默认使用内置音频后端。可替换为自定义音频后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private AudioServiceHandlerConfig m_HandlerConfig = new UnityAudioHandlerConfig();
        /// <summary>音频后端配置（纯数据，经 <see cref="AudioServiceHandlerConfig.CreateHandler"/> 创建处理器实例）。</summary>
        public static AudioServiceHandlerConfig AudioServiceHandlerConfig => Instance.m_HandlerConfig;

        [Tooltip("如果不配置 AudioGroupConfigs，则会从 AudioMixer 读取音轨配置")]
        [SerializeField] private AudioMixer m_AudioMixer;
        /// <summary>音频混音器</summary>
        public static AudioMixer AudioMixer => Instance.m_AudioMixer;

        [SerializeField] private AudioGroupConfig[] m_AudioGroupConfigs;
        /// <summary>音轨配置</summary>
        public static AudioGroupConfig[] AudioGroupConfigs => Instance.m_AudioGroupConfigs;

#if UNITY_EDITOR

        private void Reset()
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