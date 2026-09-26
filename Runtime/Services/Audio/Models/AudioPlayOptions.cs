using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// <see cref="AudioService"/> 的播放选项（服务语义层：音轨/音量/淡入/独奏/时间/缓存）。
    /// <para>体量较大，属于冷路径配置对象：播放时由 <see cref="AudioAgent"/> 拆出热路径字段缓存，不整份驻留热循环。</para>
    /// <para>热路径字段：ID / Track / Volume / Loop / Persistent / Priority / Location / Attach / FadeIn* / Solo*。</para>
    /// <para>空间整形（声像/3D 衰减/多普勒/旁通/曲线）已拆入 <see cref="AudioSpatialOptions"/>
    /// ——「往哪儿播」与「怎么响」是两件事，经 <see cref="Spatial"/> 字段整体携带。</para>
    /// </summary>
    [Serializable]
    public struct AudioPlayOptions
    {
        [Tooltip("音频的 ID，用于之后再次找到该音频，eg：sound control")]
        [SerializeField] private int m_ID;
        public int ID { get => m_ID; internal set => m_ID = value; }

        // 音轨
        [Header("音轨 [Track]")]
        [Tooltip("播放音频的音轨")]
        [SerializeField] private EAudioTrack m_AudioTrack;
        public EAudioTrack AudioTrack { get => m_AudioTrack; set => m_AudioTrack = value; }

        [Tooltip("如果不想在任意预设音轨上播放，则在此音频组上播放音频")]
        [SerializeField] private AudioMixerGroup m_AudioGroup;
        public AudioMixerGroup AudioGroup { get => m_AudioGroup; internal set => m_AudioGroup = value; }

        [Tooltip("如果不想从音频系统的音频池中选择，则可以在此处指定它")]
        [SerializeField] private AudioSource m_RecycleAudioSource;
        public AudioSource RecycleAudioSource { get => m_RecycleAudioSource; internal set => m_RecycleAudioSource = value; }

        // 音频
        [Header("音频 [Sound]")]

        [Tooltip("音量")]
        [Range(0f, 2f)]
        [SerializeField] private float m_Volume;
        public float Volume { get => m_Volume; set => m_Volume = value; }

        [Tooltip("音调。播放音频时速度的变化量，默认值1，表示正常的播放速度。（当<1时，慢速播放；当>1时，快速播放。速度越快，音调越高。）")]
        [Range(-3f, 3f)]
        [SerializeField] private float m_Pitch;
        public float Pitch { get => m_Pitch; set => m_Pitch = value; }

        [Tooltip("音频是否循环")]
        [SerializeField] private bool m_Loop;
        public bool Loop { get => m_Loop; internal set => m_Loop = value; }

        // 其他
        [Tooltip("转到另一个场景时是否应继续播放此音频")]
        [SerializeField] private bool m_Persistent;
        public bool Persistent { get => m_Persistent; internal set => m_Persistent = value; }

        [Tooltip("如果未完成播放不会自动回收（禁用音频源以便其他音频使用）")]
        [SerializeField] private bool m_DoNotAutoRecycleIfNotDonePlaying;
        public bool DoNotAutoRecycleIfNotDonePlaying { get => m_DoNotAutoRecycleIfNotDonePlaying; internal set => m_DoNotAutoRecycleIfNotDonePlaying = value; }

        [Header("缓存 [Clip Cache]")]
        [Tooltip("路径播放的 Clip 缓存策略；Default 回落到服务设置")]
        [SerializeField] private EAudioCachePolicy m_CachePolicy;
        public EAudioCachePolicy CachePolicy { get => m_CachePolicy; set => m_CachePolicy = value; }

        // 时间
        [Header("时间 [Time]")]
        [Tooltip("播放前的延迟(以秒为单位)")]
        [SerializeField] private float m_InitialDelay;
        public float InitialDelay { get => m_InitialDelay; set => m_InitialDelay = value;}

        [Tooltip("开始播放音频的时间（以秒为单位）")]
        [SerializeField] private float m_PlaybackTime;
        public float PlaybackTime { get => m_PlaybackTime; set => m_PlaybackTime = value; }
        [Tooltip("播放音频的持续时间（以秒为单位），一般需要配置是用于提前结束音频的播放")]
        [SerializeField] private float m_PlaybackDuration;
        public float PlaybackDuration { get => m_PlaybackDuration; set => m_PlaybackDuration = value; }

        // 淡入
        [Header("淡入 [Fade In]")]
        [Tooltip("是否在播放时淡入此音频")]
        [SerializeField] private bool m_FadeInOnPlay;
        public bool FadeInOnPlay { get => m_FadeInOnPlay; internal set => m_FadeInOnPlay = value; }
        [Tooltip("开始淡入的初始音量")]
        [ShowIf(nameof(m_FadeInOnPlay))]
        [SerializeField] private float m_FadeInInitialVolume;
        public float FadeInInitialVolume { get => m_FadeInInitialVolume; internal set => m_FadeInInitialVolume = value; }
        [Tooltip("音频淡入的持续时间（以秒为单位）")]
        [ShowIf(nameof(m_FadeInOnPlay))]
        [SerializeField] private float m_FadeInDuration;
        public float FadeInDuration { get => m_FadeInDuration; internal set => m_FadeInDuration = value; }
        [FormerlySerializedAs("m_FadeInTween")]
        [Tooltip("音频淡入的补间动画")]
        [ShowIf(nameof(m_FadeInOnPlay))]
        [SerializeField] private TweenEase m_FadeInTweenEase;
        public TweenEase FadeInTweenEase { get => m_FadeInTweenEase; internal set => m_FadeInTweenEase = value; }

        // 独奏
        [Header("独奏 [Solo]")]
        [Tooltip("AudioSource 是否应在其目标音轨上以 Solo 模式播放。如果是，则当该音频开始播放时，该音轨上的所有其他音频将被静音")]
        [ToggleLeft, OnValueChanged(nameof(OnSoloSingleTrackChanged))]
        [SerializeField] private bool m_SoloSingleTrack;
        private void OnSoloSingleTrackChanged(bool value) { if (value) { m_SoloAllTracks = false;} }
        public bool SoloSingleTrack { get => m_SoloSingleTrack; set => m_SoloSingleTrack = value;}
        [Tooltip("AudioSource 是否应在所有其他音轨上以 Solo 模式播放。如果是，则当此音频开始播放时，所有其他音轨都将静音")]
        [ToggleLeft, OnValueChanged(nameof(OnSoloAllTracksChanged))]
        [SerializeField] private bool m_SoloAllTracks;
        private void OnSoloAllTracksChanged(bool value) { if (value) { m_SoloSingleTrack = false;} }
        public bool SoloAllTracks { get => m_SoloAllTracks; set => m_SoloAllTracks = value;}
        [Tooltip("如果在 Solo 独奏模式下，AutoUnSoloOnEnd 将在音频停止播放后自动取消静音")]
        [ShowIf("@" + nameof(m_SoloSingleTrack) + " || " + nameof(m_SoloAllTracks))]
        [SerializeField] private bool m_AutoUnSoloOnEnd;
        public bool AutoUnSoloOnEnd { get => m_AutoUnSoloOnEnd; set => m_AutoUnSoloOnEnd = value;}

        // 空间设置
        [Header("空间设置 [Spatial Settings]")]
        /// <summary>
        /// 空间整形选项（2D 声像 / 3D 衰减、多普勒、混响与自定义曲线）。
        /// <para>公共字段而非属性（同 <see cref="AudioPlayColdParams"/> 的字段风格）：消费端大量
        /// <c>options.Spatial.SpatialBlend = x</c> 成员式赋值，属性 getter 返回结构体副本会把它们全部变成 CS1612。</para>
        /// </summary>
        public AudioSpatialOptions Spatial;

        [Tooltip("此音频可以“附加”到该 Transform 并在播放时跟随它")]
        [SerializeField] private Transform m_AttachToTransform;
        public Transform AttachToTransform { get => m_AttachToTransform; internal set => m_AttachToTransform = value; }

        [Tooltip("当播放的 AudioSource 数量多于可用硬件声道数时，Unity 将对 AudioSource 进行虚拟化处理。先对优先级（和可听度）最低的 AudioSource 进行虚拟化处理。优先级为 0 到 255 之间的整数。0 = 最高优先级，255 = 最低优先级。")]
        [Range(0, 256)]
        [SerializeField] private int m_Priority;
        public int Priority { get => m_Priority; internal set => m_Priority = value; }

        [Tooltip("播放音频的位置")]
        [SerializeField] private Vector3 m_Location;
        public Vector3 Location { get => m_Location; set => m_Location = value; }

        /// <summary>
        /// 提取 16 字节热路径请求。
        /// </summary>
        public readonly AudioPlayRequest ToRequest()
        {
            EAudioPlayFlags flags = EAudioPlayFlags.None;
            if (m_Loop) flags |= EAudioPlayFlags.Loop;
            if (m_Persistent) flags |= EAudioPlayFlags.Persistent;
            if (m_FadeInOnPlay) flags |= EAudioPlayFlags.FadeInOnPlay;
            if (m_SoloSingleTrack) flags |= EAudioPlayFlags.SoloSingleTrack;
            if (m_SoloAllTracks) flags |= EAudioPlayFlags.SoloAllTracks;
            if (m_AutoUnSoloOnEnd) flags |= EAudioPlayFlags.AutoUnSoloOnEnd;
            if (m_DoNotAutoRecycleIfNotDonePlaying) flags |= EAudioPlayFlags.DoNotAutoRecycle;

            byte priority = (byte)Mathf.Clamp(m_Priority, 0, 255);
            return new AudioPlayRequest(m_ID, m_Volume, m_Pitch, m_AudioTrack, priority, flags);
        }

        /// <summary>
        /// 默认选项，旨在适应最常见的情况。
        /// 使用选项时，最好仅覆盖需要的内容。
        ///
        /// <example>
        /// 初始化一个新的本地 AudioPlayOptions，覆盖它的循环、位置和音轨设置，并使用它调用一个播放事件
        ///
        /// <code>
        /// AudioPlayOptions options = AudioPlayOptions.Default;
        /// options.loop = Loop;
        /// options.location = Vector3.zero;
        /// options.AudioTrack = AudioTracks.Music;
        ///
        /// AudioService.Play(options);
        /// </code>
        /// </example>>
        ///
        /// </summary>
        public static AudioPlayOptions Default => new AudioPlayOptions()
        {
            m_ID = 0,
            m_AudioTrack = EAudioTrack.Sfx,
            m_AudioGroup = null,
            m_RecycleAudioSource = null,

            m_Volume = 1.0f,
            m_Pitch = 1f,
            m_Loop = false,

            m_Persistent = false,
            m_DoNotAutoRecycleIfNotDonePlaying = true,

            m_InitialDelay = 0f,
            m_PlaybackTime = 0f,
            m_PlaybackDuration = 0f,

            m_FadeInOnPlay = false,
            m_FadeInInitialVolume = 0f,
            m_FadeInDuration = 1f,
            m_FadeInTweenEase = new TweenEase(TweenUtility.EEase.InOutQuart),

            m_SoloSingleTrack = false,
            m_SoloAllTracks = false,
            m_AutoUnSoloOnEnd = false,
            m_Priority = 128,

            Spatial = AudioSpatialOptions.Default,
            m_AttachToTransform = null,
            m_Location = Vector3.zero,
        };

        /// <summary>
        /// 创建一个用于播放指定音轨的 AudioPlayOptions。
        /// </summary>
        /// <param name="track">目标音轨。</param>
        /// <returns>配置好的 AudioPlayOptions。</returns>
        public static AudioPlayOptions Create(EAudioTrack track = EAudioTrack.Sfx)
        {
            return new AudioPlayOptions
            {
                m_AudioTrack = track,
                m_Volume = 1f,
                m_Pitch = 1f,
                m_DoNotAutoRecycleIfNotDonePlaying = true,
                Spatial = AudioSpatialOptions.Default,
            };
        }

        /// <summary>
        /// 创建一个循环播放指定音轨的 AudioPlayOptions。
        /// </summary>
        /// <param name="track">目标音轨。</param>
        /// <returns>配置好的 AudioPlayOptions。</returns>
        public static AudioPlayOptions CreateLooping(EAudioTrack track = EAudioTrack.Music)
        {
            return new AudioPlayOptions
            {
                m_AudioTrack = track,
                m_Volume = 1f,
                m_Pitch = 1f,
                m_Loop = true,
                m_Persistent = true,
                m_DoNotAutoRecycleIfNotDonePlaying = true,
                Spatial = AudioSpatialOptions.Default,
            };
        }

        /// <summary>
        /// 创建一个带淡入效果的 AudioPlayOptions。
        /// </summary>
        /// <param name="track">目标音轨。</param>
        /// <param name="duration">淡入持续时间。</param>
        /// <param name="initialVolume">初始音量。</param>
        /// <param name="finalVolume">最终音量。</param>
        /// <returns>配置好的 AudioPlayOptions。</returns>
        public static AudioPlayOptions CreateWithFade(EAudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f)
        {
            return new AudioPlayOptions
            {
                m_AudioTrack = track,
                m_Volume = finalVolume,
                m_Pitch = 1f,
                m_FadeInOnPlay = true,
                m_FadeInDuration = duration,
                m_FadeInInitialVolume = initialVolume,
                m_FadeInTweenEase = new TweenEase(TweenUtility.EEase.InOutQuart),
                m_DoNotAutoRecycleIfNotDonePlaying = true,
                Spatial = AudioSpatialOptions.Default,
            };
        }
    }
}