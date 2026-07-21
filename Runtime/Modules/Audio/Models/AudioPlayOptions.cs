using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// <see cref="AudioModule"/>的播放选项
    /// </summary>
    [Serializable]
    public struct AudioPlayOptions
    {
        [HideInInspector]
        [SerializeField] private bool m_Initialized;
        public bool Initialized { get => m_Initialized; internal set => m_Initialized = value; }

        [Tooltip("音频的 ID，用于之后再次找到该音频，eg：sound control")]
        [SerializeField] private int m_ID;
        public int ID { get => m_ID; internal set => m_ID = value; }

        // 音轨
        [Header("音轨 [Track]")]
        [Tooltip("播放音频的音轨")]
        [SerializeField] private AudioTrack m_AudioTrack;
        public AudioTrack AudioTrack { get => m_AudioTrack; set => m_AudioTrack = value; }

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
        [Tooltip("声像。以立体声方式（左或右）平移音频。这仅适用于单声道或立体声的音频")]
        [Range(-1f, 1f)]
        [SerializeField] private float m_PanStereo;
        public float PanStereo { get => m_PanStereo; internal set => m_PanStereo = value; }
        [Tooltip("AudioSource 受 3D 空间化计算（衰减、多普勒等）影响的程度。0.0 使音频全 2D，1.0 使其全 3D")]
        [Range(0f, 1f)]
        [SerializeField] private float m_SpatialBlend;
        public float SpatialBlend { get => m_SpatialBlend; internal set => m_SpatialBlend = value; }
        [Tooltip("此音频可以“附加”到该 Transform 并在播放时跟随它")]
        [SerializeField] private Transform m_AttachToTransform;
        public Transform AttachToTransform { get => m_AttachToTransform; internal set => m_AttachToTransform = value; }

        // 旁通
        [Tooltip("音源滤波开关，是否打开音频特效（从滤波器 filter 组件或全局监听器滤波器 listener filter 应用）")]
        [SerializeField] private bool m_BypassEffects;
        public bool BypassEffects { get => m_BypassEffects; set => m_BypassEffects = value;}
        [Tooltip("在 AudioListener 上设置全局效果时，不会将其应用于 AudioSource 生成的音频信号。如果 AudioSource 正在播放到混音器组，则不适用")]
        [SerializeField] private bool m_BypassListenerEffects;
        public bool BypassListenerEffects { get => m_BypassListenerEffects; set => m_BypassListenerEffects = value;}
        [Tooltip("不将来自 AudioSource 的信号发送到与混响区域关联的全局混响中")]
        [SerializeField] private bool m_BypassReverbZones;
        public bool BypassReverbZones { get => m_BypassReverbZones; set => m_BypassReverbZones = value;}
        [Tooltip("当播放的 AudioSource 数量多于可用硬件声道数时，Unity 将对 AudioSource 进行虚拟化处理。先对优先级（和可听度）最低的 AudioSource 进行虚拟化处理。优先级为 0 到 255 之间的整数。0 = 最高优先级，255 = 最低优先级。")]
        [Range(0, 256)]
        [SerializeField] private int m_Priority;
        public int Priority { get => m_Priority; internal set => m_Priority = value; }
        [Tooltip("将 AudioSource 的信号混合到与混响区域相关联的全局混响中的量")]
        [Range(0f, 1.1f)]
        [SerializeField] private float m_ReverbZoneMix;
        public float ReverbZoneMix { get => m_ReverbZoneMix; internal set => m_ReverbZoneMix = value; }

        // 3D音频设置
        [Header("3D音频设置 [3D Sound Settings]")]
        [Tooltip("设置音频源应用多普勒效果的程度（如果设置为 0，则不应用任意效果）。")]
        [Range(0f, 5f)]
        [SerializeField] private float m_DopplerLevel;
        public float DopplerLevel { get => m_DopplerLevel; internal set => m_DopplerLevel = value; }
        [Tooltip("播放音频的位置")]
        [SerializeField] private Vector3 m_Location;
        public Vector3 Location { get => m_Location; set => m_Location = value; }
        [Tooltip("设置 3D 立体声或多声道音频在扬声器空间中的传播角度（以度为单位）。")]
        [Range(0, 360)]
        [SerializeField] private int m_Spread;
        public int Spread { get => m_Spread; internal set => m_Spread = value; }
        [Tooltip("音频随距离的衰减方式。Logarithmic(对数，现实世界的衰减)、Linear(线性) 和Custom(自定义)。")]
        [SerializeField] private AudioRolloffMode m_RolloffMode;
        public AudioRolloffMode RolloffMode { get => m_RolloffMode; internal set => m_RolloffMode = value; }
        [Tooltip("音量停止增大的最小距离")]
        [SerializeField] private float m_MinDistance;
        public float MinDistance { get => m_MinDistance; internal set => m_MinDistance = value; }
        [Tooltip("（对数衰减）音频停止衰减的最大距离")]
        [SerializeField] private float m_MaxDistance;
        public float MaxDistance { get => m_MaxDistance; internal set => m_MaxDistance = value; }

        [Tooltip("使用自定义音量衰减曲线")]
        [SerializeField] private bool m_UseCustomRolloffCurve;
        public bool UseCustomRolloffCurve { get => m_UseCustomRolloffCurve; internal set => m_UseCustomRolloffCurve = value; }
        [Tooltip("定义 AudioSource 的音量如何随与 AudioListener 的距离变化而衰减。")]
        [ShowIf(nameof(m_UseCustomRolloffCurve))]
        [SerializeField] private AnimationCurve m_CustomRolloffCurve;
        public AnimationCurve CustomRolloffCurve { get => m_CustomRolloffCurve; internal set => m_CustomRolloffCurve = value; }

        [Tooltip("是否使用自定义空间混合曲线")]
        [SerializeField] private bool m_UseSpatialBlendCurve;
        public bool UseSpatialBlendCurve { get => m_UseSpatialBlendCurve; internal set => m_UseSpatialBlendCurve = value; }
        [Tooltip("如果 UseSpatialBlendCurve 为 true，则用于自定义空间混合的曲线")]
        [ShowIf(nameof(m_UseSpatialBlendCurve))]
        [SerializeField] private AnimationCurve m_SpatialBlendCurve;
        public AnimationCurve SpatialBlendCurve { get => m_SpatialBlendCurve; internal set => m_SpatialBlendCurve = value; }

        [Tooltip("是否使用自定义混响区域混音曲线")]
        [SerializeField] private bool m_UseReverbZoneMixCurve;
        public bool UseReverbZoneMixCurve { get => m_UseReverbZoneMixCurve; internal set => m_UseReverbZoneMixCurve = value; }
        [Tooltip("如果 UseReverbZoneMixCurve 为 true，则用于自定义混响区域混音的曲线")]
        [ShowIf(nameof(m_UseReverbZoneMixCurve))]
        [SerializeField] private AnimationCurve m_ReverbZoneMixCurve;
        public AnimationCurve ReverbZoneMixCurve { get => m_ReverbZoneMixCurve; internal set => m_ReverbZoneMixCurve = value; }

        [Tooltip("是否使用自定义扩散曲线")]
        [SerializeField] private bool m_UseSpreadCurve;
        public bool UseSpreadCurve { get => m_UseSpreadCurve; internal set => m_UseSpreadCurve = value; }
        [Tooltip("如果 UseSpreadCurve 为 true，则用于自定义扩散的曲线")]
        [ShowIf(nameof(m_UseSpreadCurve))]
        [SerializeField] private AnimationCurve m_SpreadCurve;
        public AnimationCurve SpreadCurve { get => m_SpreadCurve; internal set => m_SpreadCurve = value; }

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
        /// AudioPlayEvent.Trigger(options);
        /// </code>
        /// </example>>
        ///
        /// </summary>
        public static AudioPlayOptions Default => new AudioPlayOptions()
        {
            m_Initialized = true,

            m_ID = 0,
            m_AudioTrack = AudioTrack.Sfx,
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
            m_BypassEffects = false,
            m_BypassListenerEffects = false,
            m_BypassReverbZones = false,
            m_Priority = 128,
            m_ReverbZoneMix = 1f,

            m_PanStereo = 0f,
            m_SpatialBlend = 0.0f,
            m_AttachToTransform = null,

            m_DopplerLevel = 1f,
            m_Location = Vector3.zero,
            m_Spread = 0,
            m_RolloffMode = AudioRolloffMode.Logarithmic,
            m_MinDistance = 1f,
            m_MaxDistance = 500f,

            m_UseCustomRolloffCurve = false,
            m_CustomRolloffCurve = null,

            m_UseSpatialBlendCurve = false,
            m_SpatialBlendCurve = null,

            m_UseReverbZoneMixCurve = false,
            m_ReverbZoneMixCurve = null,

            m_UseSpreadCurve = false,
            m_SpreadCurve = null
        };

        /// <summary>
        /// 创建一个用于播放指定音轨的 AudioPlayOptions。
        /// </summary>
        /// <param name="track">目标音轨。</param>
        /// <returns>配置好的 AudioPlayOptions。</returns>
        public static AudioPlayOptions Create(AudioTrack track = AudioTrack.Sfx)
        {
            return new AudioPlayOptions
            {
                m_Initialized = true,
                m_AudioTrack = track,
                m_Volume = 1f,
                m_Pitch = 1f,
                m_DoNotAutoRecycleIfNotDonePlaying = true,
            };
        }

        /// <summary>
        /// 创建一个循环播放指定音轨的 AudioPlayOptions。
        /// </summary>
        /// <param name="track">目标音轨。</param>
        /// <returns>配置好的 AudioPlayOptions。</returns>
        public static AudioPlayOptions CreateLooping(AudioTrack track = AudioTrack.Music)
        {
            return new AudioPlayOptions
            {
                m_Initialized = true,
                m_AudioTrack = track,
                m_Volume = 1f,
                m_Pitch = 1f,
                m_Loop = true,
                m_Persistent = true,
                m_DoNotAutoRecycleIfNotDonePlaying = true,
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
        public static AudioPlayOptions CreateWithFade(AudioTrack track, float duration, float initialVolume = 0f, float finalVolume = 1f)
        {
            return new AudioPlayOptions
            {
                m_Initialized = true,
                m_AudioTrack = track,
                m_Volume = finalVolume,
                m_Pitch = 1f,
                m_FadeInOnPlay = true,
                m_FadeInDuration = duration,
                m_FadeInInitialVolume = initialVolume,
                m_FadeInTweenEase = new TweenEase(TweenUtility.EEase.InOutQuart),
                m_DoNotAutoRecycleIfNotDonePlaying = true,
            };
        }
    }
}