using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 播放冷路径参数——位置/跟随/淡入/时间与空间整形，仅在 <c>BeginPlayback</c> 写入 AudioSource。
    /// </summary>
    /// <remarks>与 16 字节热请求 <see cref="AudioPlayRequest"/> 分离，热循环不拷贝本类型。
    /// 可为 null：本次播放不写 AudioSource 空间整形（声像/衰减/多普勒/曲线保持该声源此前的值——声源与 Agent 一对一，宿主池复位只覆盖音量/循环/静音与遮挡滤镜），
    /// 淡入、延迟、起播点与截取时长落到各自缺省。
    /// 空间整形经单字段 <see cref="Spatial"/>（与 <see cref="AudioPlayOptions.Spatial"/> 同构）整体携带。</remarks>
    public sealed class AudioPlayColdParams
    {
        /// <summary>播放位置。</summary>
        public Vector3 Location;

        /// <summary>跟随目标。</summary>
        public Transform AttachToTransform;

        /// <summary>覆盖混音组。</summary>
        public AudioMixerGroup AudioGroup;

        /// <summary>外部指定 AudioSource。</summary>
        public AudioSource RecycleAudioSource;

        /// <summary>淡入初始音量。</summary>
        public float FadeInInitialVolume;

        /// <summary>淡入时长。</summary>
        public float FadeInDuration = 1f;

        /// <summary>淡入缓动。</summary>
        public TweenEase FadeInTweenEase;

        /// <summary>播放前延迟。</summary>
        public float InitialDelay;

        /// <summary>起播时间点。</summary>
        public float PlaybackTime;

        /// <summary>自定义播放时长（0 = 整段）。</summary>
        public float PlaybackDuration;

        /// <summary>空间整形（2D 声像 / 3D 衰减、多普勒、混响与自定义曲线）。</summary>
        /// <remarks>零值结构体等于「无多普勒、零衰减距离、无混响」，复位/缺省一律用 <see cref="AudioSpatialOptions.Default"/>。</remarks>
        public AudioSpatialOptions Spatial = AudioSpatialOptions.Default;

        /// <summary>
        /// 尽量复用实例的重置（池友好）。
        /// </summary>
        public void ResetToDefault()
        {
            Location = Vector3.zero;
            AttachToTransform = null;
            AudioGroup = null;
            RecycleAudioSource = null;
            FadeInInitialVolume = 0f;
            FadeInDuration = 1f;
            FadeInTweenEase = default;
            InitialDelay = 0f;
            PlaybackTime = 0f;
            PlaybackDuration = 0f;
            Spatial = AudioSpatialOptions.Default;
        }

        /// <summary>
        /// 从完整 <see cref="AudioPlayOptions"/> 提取冷参数（从池获取；所有权转移给调用方/Agent）。
        /// </summary>
        public static AudioPlayColdParams FromOptions(in AudioPlayOptions options)
        {
            var p = AudioPlayColdParamsPool.Acquire();
            p.Location = options.Location;
            p.AttachToTransform = options.AttachToTransform;
            p.AudioGroup = options.AudioGroup;
            p.RecycleAudioSource = options.RecycleAudioSource;
            p.FadeInInitialVolume = options.FadeInInitialVolume;
            p.FadeInDuration = options.FadeInDuration;
            p.FadeInTweenEase = options.FadeInTweenEase;
            p.InitialDelay = options.InitialDelay;
            p.PlaybackTime = options.PlaybackTime;
            p.PlaybackDuration = options.PlaybackDuration;
            p.Spatial = options.Spatial;
            return p;
        }
    }
}
