using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioSource 空间整形选项（2D 声像 / 3D 衰减、多普勒、混响与自定义曲线），对应 Unity AudioSource 的「空间设置 / 3D Sound Settings」面板。
    /// </summary>
    /// <remarks>
    /// 「往哪儿播」（Location/AttachToTransform）留在 <see cref="AudioPlayOptions"/>，「怎么响」收拢在本类型。 <br />
    /// 经 <see cref="AudioPlayColdParams.Spatial"/> 进入冷路径，仅在 BeginPlayback 时写入 AudioSource，热循环不拷贝本类型。 <br />
    /// 零值结构体等于「无多普勒、零衰减距离、无混响」，需要自定义时从 <see cref="Default"/> 起覆盖。
    /// </remarks>
    public struct AudioSpatialOptions
    {
        /// <!-- 空间设置 -->
        
        /// <summary>立体声声像（-1 左 / 1 右）。</summary>
        public float PanStereo { get; set; }

        /// <summary>3D 混合（0 全 2D / 1 全 3D）。</summary>
        public float SpatialBlend { get; set; }

        /// <summary>混响区混合量（1.0 默认，上限 1.1 允许近场增益）。</summary>
        public float ReverbZoneMix { get; set; }

        /// <!-- 旁通 -->
        
        /// <summary>是否旁通音源上的滤波器效果。</summary>
        public bool BypassEffects { get; set; }

        /// <summary>是否旁通 AudioListener 上的全局效果。</summary>
        public bool BypassListenerEffects { get; set; }

        /// <summary>是否旁通全局混响区。</summary>
        public bool BypassReverbZones { get; set; }

        /// <!-- 3D 音频设置 -->
        
        /// <summary>多普勒强度（0 无）。</summary>
        public float DopplerLevel { get; set; }

        /// <summary>扬声器空间传播角（0-360 度）。</summary>
        public int Spread { get; set; }

        /// <summary>距离衰减模式。</summary>
        public AudioRolloffMode RolloffMode { get; set; }

        /// <summary>最小衰减距离。</summary>
        public float MinDistance { get; set; }
        /// <summary>最大衰减距离。</summary>
        public float MaxDistance { get; set; }

        /// <summary>是否使用自定义音量衰减曲线。</summary>
        public bool UseCustomRolloffCurve { get; set; }
        /// <summary>自定义音量衰减曲线。</summary>
        public AnimationCurve CustomRolloffCurve { get; set; }

        /// <summary>是否使用自定义空间混合曲线。</summary>
        public bool UseSpatialBlendCurve { get; set; }
        /// <summary>自定义空间混合曲线。</summary>
        public AnimationCurve SpatialBlendCurve { get; set; }

        /// <summary>是否使用自定义混响区混合曲线。</summary>
        public bool UseReverbZoneMixCurve { get; set; }
        /// <summary>自定义混响区混合曲线。</summary>
        public AnimationCurve ReverbZoneMixCurve { get; set; }

        /// <summary>是否使用自定义扩散曲线。</summary>
        public bool UseSpreadCurve { get; set; }
        /// <summary>自定义扩散曲线。</summary>
        public AnimationCurve SpreadCurve { get; set; }

        /// <summary>声学默认值（对齐 Unity AudioSource 缺省：多普勒 1 / 最小 1 / 最大 500 / 混响 1 / 对数衰减）。</summary>
        public static AudioSpatialOptions Default => new AudioSpatialOptions
        {
            PanStereo = 0f,
            SpatialBlend = 0f,
            ReverbZoneMix = 1f,
            BypassEffects = false,
            BypassListenerEffects = false,
            BypassReverbZones = false,
            DopplerLevel = 1f,
            Spread = 0,
            RolloffMode = AudioRolloffMode.Logarithmic,
            MinDistance = 1f,
            MaxDistance = 500f,
            UseCustomRolloffCurve = false,
            CustomRolloffCurve = null,
            UseSpatialBlendCurve = false,
            SpatialBlendCurve = null,
            UseReverbZoneMixCurve = false,
            ReverbZoneMixCurve = null,
            UseSpreadCurve = false,
            SpreadCurve = null,
        };
    }
}
