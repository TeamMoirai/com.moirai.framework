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

        [Tooltip("混音快照配置：状态 → AudioMixerSnapshot 映射；Priority < 0 使用内置默认优先级。空 Snapshot 可由「从 Mixer 重建」按名自动补齐，手工非空映射优先")]
        [SerializeField] private AudioMixSnapshotEntry[] m_MixSnapshots;
        /// <summary>混音快照配置</summary>
        internal static AudioMixSnapshotEntry[] MixSnapshots => Instance.m_MixSnapshots;

        /// <summary>
        /// 从 AudioMixer 按名重建 MixSnapshots 映射（一键绑定）。
        /// <para>已有非空 Snapshot 的手工映射保留；仅补齐空缺并铺全状态。</para>
        /// </summary>
        [ContextMenu("从 Mixer 重建混音快照映射")]
        [Button("从 Mixer 重建混音快照映射")]
        public void RebuildFromMixer() => RebuildMixSnapshotsFromMixer();

        /// <summary>
        /// 重建映射并回报结果：服务已在跑时顺带把新映射登记进状态机，避免"改了配置但当前会话毫无变化"。
        /// </summary>
        /// <returns>本次由自动绑定补齐的条目数；<c>0</c> 表示 Mixer 里没有可按名对上的快照。</returns>
        public int RebuildMixSnapshotsFromMixer()
        {
            var mixer = m_AudioMixer;
            if (mixer == null)
            {
                AudioWarnOnce.Warning("settings:rebuild-no-mixer",
                    "[AudioServiceSettings] 未配置 AudioMixer，无法重建混音快照映射。");
                return 0;
            }

            var snapshots = AudioMixStateMachine.CollectMixerSnapshots(mixer);
            var names = new string[snapshots.Length];
            for (int i = 0; i < snapshots.Length; i++)
            {
                names[i] = snapshots[i] != null ? snapshots[i].name : null;
            }

            var states = (EMixSnapshot[])Enum.GetValues(typeof(EMixSnapshot));
            var existing = m_MixSnapshots ?? Array.Empty<AudioMixSnapshotEntry>();

            // 重复 State 原先是"首个胜出、其余静默丢弃"——资产里真有重复时必须说出来，
            // 否则音效师改了后一行却毫无效果，且无从归因。
            int duplicates = 0;
            for (int a = 0; a < existing.Length; a++)
            {
                for (int b = a + 1; b < existing.Length; b++)
                {
                    if (existing[a] != null && existing[b] != null && existing[a].State == existing[b].State) duplicates++;
                }
            }

            if (duplicates > 0)
            {
                AudioWarnOnce.Warning("settings:mix-snapshot-duplicate",
                    "[AudioServiceSettings] MixSnapshots 里有 {0} 处重复 State，重建时每个状态只保留第一条。", duplicates);
            }

            var list = new System.Collections.Generic.List<AudioMixSnapshotEntry>(states.Length);
            int filled = 0;

            for (int s = 0; s < states.Length; s++)
            {
                var state = states[s];
                AudioMixSnapshotEntry kept = null;
                for (int e = 0; e < existing.Length; e++)
                {
                    if (existing[e] != null && existing[e].State == state)
                    {
                        kept = existing[e];
                        break;
                    }
                }

                var entry = kept ?? new AudioMixSnapshotEntry { State = state };
                if (entry.Snapshot == null)
                {
                    int index = AudioMixStateMachine.ResolveSnapshotIndex(names, state);
                    if (index >= 0)
                    {
                        entry.Snapshot = snapshots[index];
                        filled++;
                    }
                }

                list.Add(entry);
            }

            m_MixSnapshots = list.ToArray();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            // 运行期改动要当场进状态机；服务未初始化时 AudioMixService 自行跳过
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                if (entry.Snapshot != null) AudioMixService.RegisterSnapshot(entry.State, entry.Snapshot, entry.Priority);
            }

            if (filled == 0)
            {
                AudioWarnOnce.Warning("settings:rebuild-nothing-bound",
                    "[AudioServiceSettings] Mixer 里没有与 EMixSnapshot 同名的 Snapshot，自动绑定为 0；请改快照名或手工映射。");
            }

            return filled;
        }

        [Header("自动 Ducking [Auto Ducking]")]
        [Tooltip("Voice 音轨有声在播时自动切到 Dialogue 快照，播完自动回落。需先在 MixSnapshots 里注册 Dialogue 快照，否则切换为空操作。")]
        [SerializeField] private bool m_AutoDuckingOnVoice;
        /// <summary>是否启用 Voice 驱动的自动 Ducking。</summary>
        internal static bool AutoDuckingOnVoice => Instance.m_AutoDuckingOnVoice;

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

        // AudioClip 缓存（Lease + LRU + TTL + Pin）
        [Header("Clip 缓存 [Clip Cache]")]
        [Tooltip("Clip 缓存最大条目数（超出后从 LRU 驱逐无引用条目）")]
        [SerializeField, Min(1)] private int m_ClipCacheCapacity = AudioClipCache.DefaultCapacity;
        /// <summary>Clip 缓存容量。</summary>
        internal static int ClipCacheCapacity => Instance.m_ClipCacheCapacity;

        [Tooltip("用后缓存 TTL（秒）；0 表示不按时间驱逐")]
        [SerializeField, Min(0f)] private float m_ClipCacheTtl = AudioClipCache.DefaultTtl;
        /// <summary>Clip 缓存 TTL 秒数。</summary>
        internal static float ClipCacheTtl => Instance.m_ClipCacheTtl;

        [Tooltip("路径播放默认缓存策略（None=用完即弃，Ttl=用后缓存，Pin=常驻）")]
        [SerializeField] private AudioCachePolicy m_DefaultClipCachePolicy = AudioCachePolicy.Ttl;
        /// <summary>默认 Clip 缓存策略。</summary>
        internal static AudioCachePolicy DefaultClipCachePolicy => Instance.m_DefaultClipCachePolicy;

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
