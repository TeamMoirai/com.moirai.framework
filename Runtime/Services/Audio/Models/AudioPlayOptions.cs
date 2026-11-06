using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// <see cref="AudioService"/> 的播放选项（服务语义层：音轨/音量/淡入/独奏/时间/缓存）。
    /// </summary>
    /// <remarks>
    /// 冷路径配置对象： 播放时由 <see cref="AudioAgent"/> 拆出热路径字段（ID / Track / Volume / Loop / Persistent / Priority / Location / <br />
    /// Attach / FadeIn* / Solo*）缓存，不整份驻留热循环。 <br />
    /// 空间整形（声像/3D 衰减/多普勒/旁通/曲线）经 <see cref="Spatial"/> 字段整体携带，与「往哪儿播」分离，见 <see cref="AudioSpatialOptions"/>。
    /// </remarks>
    public struct AudioPlayOptions
    {
        /// <summary>音频 ID，用于之后再次找到该音频（eg：sound control）。</summary>
        public int ID { get; set; }

        /// <!-- 音轨 -->
        
        /// <summary>播放音频的音轨。</summary>
        public EAudioTrack AudioTrack { get; set; }

        /// <summary>非空时在该混音组上播放，而不是走预设音轨。</summary>
        public AudioMixerGroup AudioGroup { get; set; }

        /// <summary>非空时直接用它播放，而不从音频系统的音频池中选择。</summary>
        public AudioSource RecycleAudioSource { get; set; }

        /// <!-- 音频 -->
        
        /// <summary>音量。</summary>
        public float Volume { get; set; }

        /// <summary>音调，播放音频时速度的变化量；1 为正常速度，小于 1 慢速播放、大于 1 快速播放且速度越快音调越高。</summary>
        public float Pitch { get; set; }

        /// <summary>音频是否循环。</summary>
        public bool Loop { get; set; }

        /// <!-- 其他 -->
        
        /// <summary>转到另一个场景时是否应继续播放此音频。</summary>
        public bool Persistent { get; set; }

        /// <summary>置位则未完成播放时不自动回收该音频源，留给自己用到播完。</summary>
        public bool DoNotAutoRecycleIfNotDonePlaying { get; internal set; }

        /// <summary>路径播放的 Clip 缓存策略；Default 回落到服务设置。</summary>
        public EAudioCachePolicy CachePolicy { get; set; }

        /// <!-- 时间 -->
        
        /// <summary>播放前的延迟（以秒为单位）。</summary>
        public float InitialDelay { get; set; }

        /// <summary>开始播放音频的时间（以秒为单位）。</summary>
        public float PlaybackTime { get; set; }

        /// <summary>播放音频的持续时间（以秒为单位），一般用于提前结束音频的播放。</summary>
        public float PlaybackDuration { get; set; }

        /// <!-- 淡入 -->
        
        /// <summary>是否在播放时淡入此音频。</summary>
        public bool FadeInOnPlay { get; set; }
        /// <summary>开始淡入的初始音量。</summary>
        public float FadeInInitialVolume { get; set; }
        /// <summary>音频淡入的持续时间（以秒为单位）。</summary>
        public float FadeInDuration { get; set; }
        /// <summary>音频淡入的补间动画。</summary>
        public TweenEase FadeInTweenEase { get; set; }

        /// <!-- 独奏 -->

        /// <summary>独奏范围；开播时静音该范围内的其他音频。</summary>
        public EAudioSoloMode SoloMode { get; set; }
        /// <summary>处于 Solo 模式时，音频停止播放后是否自动取消静音。</summary>
        public bool AutoUnSoloOnEnd { get; set; }
        
        /// <summary>空间整形选项（2D 声像 / 3D 衰减、多普勒、混响与自定义曲线）。</summary>
        /// <remarks>
        /// 公共字段而非属性：消费端大量 <c>options.Spatial.SpatialBlend = x</c> 成员式赋值，属性 getter 返回结构体副本会把它们全部变成 CS1612。
        /// </remarks>
        public AudioSpatialOptions Spatial;

        /// <summary>此音频可以“附加”到该 Transform 并在播放时跟随它。</summary>
        public Transform AttachToTransform { get; internal set; }

        /// <summary>播放优先级，0 最高、255 最低；可用硬件声道不足时，Unity 先虚拟化处理优先级（和可听度）最低的 AudioSource。</summary>
        public int Priority { get; set; }

        /// <summary>播放音频的位置。</summary>
        public Vector3 Location { get; set; }

        /// <summary>
        /// 提取 16 字节热路径请求。
        /// </summary>
        public readonly AudioPlayRequest ToRequest()
        {
            EAudioPlayFlags flags = EAudioPlayFlags.None;
            if (Loop) flags |= EAudioPlayFlags.Loop;
            if (Persistent) flags |= EAudioPlayFlags.Persistent;
            if (FadeInOnPlay) flags |= EAudioPlayFlags.FadeInOnPlay;
            if (SoloMode == EAudioSoloMode.SingleTrack) flags |= EAudioPlayFlags.SoloSingleTrack;
            if (SoloMode == EAudioSoloMode.AllTracks) flags |= EAudioPlayFlags.SoloAllTracks;
            if (AutoUnSoloOnEnd) flags |= EAudioPlayFlags.AutoUnSoloOnEnd;
            if (DoNotAutoRecycleIfNotDonePlaying) flags |= EAudioPlayFlags.DoNotAutoRecycle;

            byte priority = (byte)Mathf.Clamp(Priority, 0, 255);
            return new AudioPlayRequest(ID, Volume, Pitch, AudioTrack, priority, flags);
        }

        /// <summary>默认选项，适用于最常见的情况；使用时最好只覆盖需要的内容。</summary>
        /// <example>
        /// <code lang="csharp">
        /// AudioPlayOptions options = AudioPlayOptions.Default;
        /// options.Loop = true;
        /// options.Location = Vector3.zero;
        /// options.AudioTrack = EAudioTrack.Music;
        /// AudioService.Play(clip, options);
        /// </code>
        /// </example>
        public static AudioPlayOptions Default => new AudioPlayOptions()
        {
            ID = 0,
            AudioTrack = EAudioTrack.Sfx,
            AudioGroup = null,
            RecycleAudioSource = null,

            Volume = 1.0f,
            Pitch = 1f,
            Loop = false,

            Persistent = false,
            DoNotAutoRecycleIfNotDonePlaying = true,

            InitialDelay = 0f,
            PlaybackTime = 0f,
            PlaybackDuration = 0f,

            FadeInOnPlay = false,
            FadeInInitialVolume = 0f,
            FadeInDuration = 1f,
            FadeInTweenEase = new TweenEase(TweenUtility.EEase.InOutQuart),

            SoloMode = EAudioSoloMode.None,
            AutoUnSoloOnEnd = false,
            Priority = 128,

            Spatial = AudioSpatialOptions.Default,
            AttachToTransform = null,
            Location = Vector3.zero,
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
                AudioTrack = track,
                Volume = 1f,
                Pitch = 1f,
                DoNotAutoRecycleIfNotDonePlaying = true,
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
                AudioTrack = track,
                Volume = 1f,
                Pitch = 1f,
                Loop = true,
                Persistent = true,
                DoNotAutoRecycleIfNotDonePlaying = true,
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
                AudioTrack = track,
                Volume = finalVolume,
                Pitch = 1f,
                FadeInOnPlay = true,
                FadeInDuration = duration,
                FadeInInitialVolume = initialVolume,
                FadeInTweenEase = new TweenEase(TweenUtility.EEase.InOutQuart),
                DoNotAutoRecycleIfNotDonePlaying = true,
                Spatial = AudioSpatialOptions.Default,
            };
        }
    }
}
