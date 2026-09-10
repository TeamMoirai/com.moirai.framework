using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 播放冷路径参数——曲线/空间/旁通/延迟等，仅在 <c>BeginPlayback</c> 写入 AudioSource。
    /// <para>与 16 字节热请求 <see cref="AudioPlayRequest"/> 分离，热循环不拷贝本类型。</para>
    /// <para>可为 null：Agent 使用默认空间/无曲线。</para>
    /// </summary>
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

        /// <summary>立体声声像。</summary>
        public float PanStereo;

        /// <summary>3D 混合。</summary>
        public float SpatialBlend;

        /// <summary>多普勒强度。</summary>
        public float DopplerLevel = 1f;

        /// <summary>混响混合。</summary>
        public float ReverbZoneMix = 1f;

        /// <summary>最小距离。</summary>
        public float MinDistance = 1f;

        /// <summary>最大距离。</summary>
        public float MaxDistance = 500f;

        /// <summary>扩散角。</summary>
        public int Spread;

        /// <summary>衰减模式。</summary>
        public AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;

        public bool BypassEffects;
        public bool BypassListenerEffects;
        public bool BypassReverbZones;

        public bool UseSpreadCurve;
        public bool UseCustomRolloffCurve;
        public bool UseSpatialBlendCurve;
        public bool UseReverbZoneMixCurve;

        public AnimationCurve SpreadCurve;
        public AnimationCurve CustomRolloffCurve;
        public AnimationCurve SpatialBlendCurve;
        public AnimationCurve ReverbZoneMixCurve;

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
            PanStereo = 0f;
            SpatialBlend = 0f;
            DopplerLevel = 1f;
            ReverbZoneMix = 1f;
            MinDistance = 1f;
            MaxDistance = 500f;
            Spread = 0;
            RolloffMode = AudioRolloffMode.Logarithmic;
            BypassEffects = false;
            BypassListenerEffects = false;
            BypassReverbZones = false;
            UseSpreadCurve = false;
            UseCustomRolloffCurve = false;
            UseSpatialBlendCurve = false;
            UseReverbZoneMixCurve = false;
            SpreadCurve = null;
            CustomRolloffCurve = null;
            SpatialBlendCurve = null;
            ReverbZoneMixCurve = null;
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
            p.PanStereo = options.PanStereo;
            p.SpatialBlend = options.SpatialBlend;
            p.DopplerLevel = options.DopplerLevel;
            p.ReverbZoneMix = options.ReverbZoneMix;
            p.MinDistance = options.MinDistance;
            p.MaxDistance = options.MaxDistance;
            p.Spread = options.Spread;
            p.RolloffMode = options.RolloffMode;
            p.BypassEffects = options.BypassEffects;
            p.BypassListenerEffects = options.BypassListenerEffects;
            p.BypassReverbZones = options.BypassReverbZones;
            p.UseSpreadCurve = options.UseSpreadCurve;
            p.UseCustomRolloffCurve = options.UseCustomRolloffCurve;
            p.UseSpatialBlendCurve = options.UseSpatialBlendCurve;
            p.UseReverbZoneMixCurve = options.UseReverbZoneMixCurve;
            p.SpreadCurve = options.SpreadCurve;
            p.CustomRolloffCurve = options.CustomRolloffCurve;
            p.SpatialBlendCurve = options.SpatialBlendCurve;
            p.ReverbZoneMixCurve = options.ReverbZoneMixCurve;
            return p;
        }
    }
}
