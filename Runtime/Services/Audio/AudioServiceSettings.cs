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
        [SerializeReference] private AudioServiceHandler m_AudioServiceHandler = AudioService.CreateDefaultHandler();
        /// <summary>音频处理器（后端）。</summary>
        internal static AudioServiceHandler AudioServiceHandler => Instance.m_AudioServiceHandler;

        [Tooltip("如果不配置 AudioGroupConfigs，则会从 AudioMixer 读取音轨配置")]
        [SerializeField] private AudioMixer m_AudioMixer;
        /// <summary>音频混音器</summary>
        internal static AudioMixer AudioMixer => Instance.m_AudioMixer;

        [SerializeField] private AudioGroupConfig[] m_AudioGroupConfigs;
        /// <summary>音轨配置</summary>
        internal static AudioGroupConfig[] AudioGroupConfigs => Instance.m_AudioGroupConfigs;

        [Tooltip("混音快照配置：状态 → AudioMixerSnapshot 映射；Priority < 0 使用内置默认优先级")]
        [SerializeField] private AudioMixSnapshotEntry[] m_MixSnapshots;
        /// <summary>混音快照配置</summary>
        internal static AudioMixSnapshotEntry[] MixSnapshots => Instance.m_MixSnapshots;

        // AudioAgentHostPool Bootstrap
        [Header("池预热引导 [Pool Bootstrap]")]
        
        [Tooltip("是否在服务 OnInit 时预热 AudioAgent 宿主栈池")]
        [SerializeField] private bool m_WarmupAudioHostPool = true;
        /// <summary>是否预热宿主栈池。</summary>
        internal static bool WarmupAudioHostPool => Instance.m_WarmupAudioHostPool;

        [Tooltip("AudioAgent 宿主栈池预热数量")]
        [ShowIf(nameof(m_WarmupAudioHostPool))]
        [SerializeField] private int m_AudioHostWarmupCount = 8;
        /// <summary>宿主栈池预热数量。</summary>
        internal static int AudioHostWarmupCount => Instance.m_AudioHostWarmupCount;

#if UNITY_EDITOR

        private void Reset()
        {
            // 从 Resources 中读取默认 AudioMixer
            m_AudioMixer = Resources.Load<AudioMixer>("AudioMixer");
            if (m_AudioMixer != null)
            {
                // 仅保留能映射到 EAudioTrack 的混音组，避免嵌套组（如 Player SFX）挤占音轨造成重复
                var audioMixerGroups = m_AudioMixer.FindMatchingGroups("Master/");
                var configs = new System.Collections.Generic.List<AudioGroupConfig>(audioMixerGroups.Length);
                for (int i = 0; i < audioMixerGroups.Length; i++)
                {
                    if (!Enum.TryParse<EAudioTrack>(audioMixerGroups[i].name, out var audioTrack)) continue;

                    var config = new AudioGroupConfig();
                    config.AudioMixerGroup = audioMixerGroups[i];
                    config.AudioTrack = audioTrack;
                    configs.Add(config);
                }

                m_AudioGroupConfigs = configs.ToArray();
            }
        }

#endif
    }
}
