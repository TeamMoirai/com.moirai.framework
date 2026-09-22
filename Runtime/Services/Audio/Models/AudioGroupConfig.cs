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

        [Tooltip("默认音量（线性 0~1，1 = 0dB）")]
        [Range(0, MAXIMAL_VOLUME)]
        [SerializeField] private float m_DefaultVolume = 1f;

        [Tooltip("将归一化音量值转换为混音器值时要的系数（分贝转换）")]
        [SerializeField] private float m_MixerValuesMultiplier = 20f;

        [Tooltip("可同时播放的最大数量")]
        [SerializeField] private int m_MaxChannel = 3;

        [Tooltip("否可以扩展（按需创建新的音频源）")]
        [SerializeField] private bool m_CanExpand;

        [Tooltip("扩展的通道硬上限（仅 CanExpand 生效）：按平台预算分轨设置，非法值回落到默认 32")]
        [SerializeField, Min(1)] private int m_MaxChannelCeiling = HARD_CHANNEL_CEILING_DEFAULT;

        // 最小音量：0 会走进 log10 而 -∞，用 -80dB 作为可表达的最底（与静音写 -80 的旧值同一条线）
        public const float MINIMAL_VOLUME = 0.0001f;

        /// <summary>
        /// 音量值域上限，<c>1</c>（线性增益，1 = 0dB 满刻度）。
        /// <para>这里过去是 <c>10</c>（≈ +20dB 提升），但只有 Unity 后端真拿得到：中间件把同一个值落到总线前
        /// <c>Clamp01</c>，于是"同一份设置换后端，音轨上限从 10 变 1"。契约统一成线性 0..1 之后，
        /// 想要工程侧留提升余量，请改 Mixer 分组的暴露参数或 <c>m_MixerValuesMultiplier</c>，
        /// 而不是让对外契约在不同后端下值域不一致。</para>
        /// </summary>
        public const float MAXIMAL_VOLUME = 1f;

        /// <summary>扩展硬上限的缺省值——保持与历史上写死的 32 一致。</summary>
        public const int HARD_CHANNEL_CEILING_DEFAULT = 32;

        /// <summary>
        /// 扩展硬上限的绝对天花板：这条轨道上限本身就是「别把通道数跑飞」的保险，
        /// 所以配置再大也不越过它（主机要更多声部应改的是 <see cref="MaxChannel"/>，不是把保险拆掉）。
        /// </summary>
        public const int HARD_CHANNEL_CEILING_MAX = 128;

        private bool _isMuted;
        private float _volume;

        // 缓存设置键 / 混音器参数名，避免属性访问与音量应用时反复分配字符串
        private string _muteSettingKey;
        private string _volumeSettingKey;
        private string _mixerVolumeParam;
        private bool _keysCached;

        /// <summary>
        /// 音频类型
        /// </summary>
        public EAudioTrack AudioTrack
        {
            get => m_AudioTrack;
            internal set
            {
                m_AudioTrack = value;
                _keysCached = false;
            }
        }

        /// <summary>
        /// mixer 中的组对象。
        /// </summary>
        public AudioMixerGroup AudioMixerGroup
        {
            get => m_AudioMixerGroup;
            internal set
            {
                m_AudioMixerGroup = value;
                _keysCached = false;
            }
        }

        /// <summary>
        /// 确保设置键与混音器参数名已缓存。
        /// </summary>
        private void EnsureCachedKeys()
        {
            if (_keysCached) return;

            _muteSettingKey = StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_MUTED, m_AudioTrack);
            _volumeSettingKey = StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_VOLUME, m_AudioTrack);
            _mixerVolumeParam = StringUtility.Format("{0}Volume",
                m_AudioMixerGroup != null ? m_AudioMixerGroup.name : m_AudioTrack.ToString());
            _keysCached = true;
        }

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
            }
        }

        /// <summary>
        /// 当前音轨的音量，线性 <c>0..1</c>。
        /// </summary>
        /// <remarks>夹取放在 setter 而不是只放在写 Mixer 的那一刻：<c>Volume</c> 是对外可读的，
        /// 让存着的值越界就会让 getter 报回一个契约外的数（旧写法正是如此，2.5 能原样读回来）。
        /// 0 保留为 0（写 Mixer 时才换算成 <see cref="MINIMAL_VOLUME"/> 对应的 -80dB），
        /// 这样 UI 拉到 0 再读回来仍是 0。</remarks>
        public float Volume
        {
            get => _volume;
            set
            {
                float volume = Mathf.Clamp01(value);
                if (Mathf.Approximately(_volume, volume)) return;

                _volume = volume;
                ApplyTrackVolume();
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
        /// 扩展的通道硬上限：只约束 <see cref="CanExpand"/> 的按需增长，不约束 <see cref="MaxChannel"/> 的预置槽位。
        /// </summary>
        /// <remarks>非正数（老资产缺字段、YAML 手改坏）回落到 <see cref="HARD_CHANNEL_CEILING_DEFAULT"/>，
        /// 过大值削到 <see cref="HARD_CHANNEL_CEILING_MAX"/>——宁可按缺省跑，也不让一个写坏的数把保险拆掉。</remarks>
        public int MaxChannelCeiling => m_MaxChannelCeiling > 0
            ? Mathf.Min(m_MaxChannelCeiling, HARD_CHANNEL_CEILING_MAX)
            : HARD_CHANNEL_CEILING_DEFAULT;

        /// <summary>
        /// 写入设置
        /// </summary>
        public void SetSettings()
        {
            EnsureCachedKeys();
            SettingUtility.SetBool(_muteSettingKey, _isMuted);
            SettingUtility.SetFloat(_volumeSettingKey, _volume);
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        public void LoadSettings()
        {
            EnsureCachedKeys();
            _isMuted = SettingUtility.GetBool(_muteSettingKey, false);
            // 存量值可能写于上限还是 10 的年代：Volume 是对外可读的，读回来就得在契约值域内
            _volume = Mathf.Clamp01(SettingUtility.GetFloat(_volumeSettingKey, m_DefaultVolume));

            ApplyTrackVolume();
        }

        /// <summary>
        /// 移除设置：回到 <see cref="m_DefaultVolume"/> 与取消静音（与 <see cref="LoadSettings"/> 的缺省回落一致）。
        /// </summary>
        public void RemoveSetting()
        {
            EnsureCachedKeys();
            SettingUtility.RemoveSetting(_muteSettingKey);
            SettingUtility.RemoveSetting(_volumeSettingKey);

            _isMuted = false;
            _volume = Mathf.Clamp01(m_DefaultVolume);
            ApplyTrackVolume();
        }

        /// <summary>
        /// 将音量应用于所属音轨
        /// </summary>
        private void ApplyTrackVolume()
        {
            if (m_AudioMixerGroup == null || m_AudioMixerGroup.audioMixer == null) return;

            EnsureCachedKeys();
            float volume = Mathf.Clamp(_isMuted ? 0f : _volume, MINIMAL_VOLUME, MAXIMAL_VOLUME);
            m_AudioMixerGroup.audioMixer.SetFloat(_mixerVolumeParam, NormalizedToMixerVolume(volume));
        }

        /// <summary>
        /// 将归一化音量转换为混音器组 db
        /// </summary>
        private float NormalizedToMixerVolume(float normalizedVolume)
        {
            return Mathf.Log10(normalizedVolume) * m_MixerValuesMultiplier;
        }
    }
}
