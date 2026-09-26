using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioSource 空间整形选项（2D 声像 / 3D 衰减、多普勒、混响与自定义曲线），
    /// 对应 Unity AudioSource Inspector 的「空间设置 / 3D Sound Settings」面板。
    /// <para>从 <see cref="AudioPlayOptions"/> 拆出：
    /// 「往哪儿播」（Location/AttachToTransform）与「怎么响」（本类型）是两件事，
    /// 前者留在播放选项里，后者收拢在这一个声学整形载体里。</para>
    /// <para>经 <see cref="AudioPlayColdParams.Spatial"/> 进入冷路径，仅在 BeginPlayback 时写入 AudioSource，
    /// 热循环不拷贝本类型；可用 <see cref="Default"/> 作为起点只覆盖需要的项。</para>
    /// </summary>
    [Serializable]
    public struct AudioSpatialOptions
    {
        // 空间设置
        [Header("空间设置 [Spatial Settings]")]
        [Tooltip("声像。以立体声方式（左或右）平移音频。这仅适用于单声道或立体声的音频")]
        [Range(-1f, 1f)]
        [SerializeField] private float m_PanStereo;
        /// <summary>立体声声像（-1 左 / 1 右）。</summary>
        public float PanStereo { get => m_PanStereo; internal set => m_PanStereo = value; }
        [Tooltip("AudioSource 受 3D 空间化计算（衰减、多普勒等）影响的程度。0.0 使音频全 2D，1.0 使其全 3D")]
        [Range(0f, 1f)]
        [SerializeField] private float m_SpatialBlend;
        /// <summary>3D 混合（0 全 2D / 1 全 3D）。</summary>
        public float SpatialBlend { get => m_SpatialBlend; internal set => m_SpatialBlend = value; }
        [Tooltip("将 AudioSource 的信号混合到与混响区域相关联的全局混响中的量")]
        [Range(0f, 1.1f)]
        [SerializeField] private float m_ReverbZoneMix;
        /// <summary>混响区混合量（1.0 默认，上限 1.1 允许近场增益）。</summary>
        public float ReverbZoneMix { get => m_ReverbZoneMix; internal set => m_ReverbZoneMix = value; }

        // 旁通
        [Tooltip("音源滤波开关，是否打开音频特效（从滤波器 filter 组件或全局监听器滤波器 listener filter 应用）")]
        [SerializeField] private bool m_BypassEffects;
        /// <summary>是否旁通音源上的滤波器效果。</summary>
        public bool BypassEffects { get => m_BypassEffects; set => m_BypassEffects = value; }
        [Tooltip("在 AudioListener 上设置全局效果时，不会将其应用于 AudioSource 生成的音频信号。如果 AudioSource 正在播放到混音器组，则不适用")]
        [SerializeField] private bool m_BypassListenerEffects;
        /// <summary>是否旁通 AudioListener 上的全局效果。</summary>
        public bool BypassListenerEffects { get => m_BypassListenerEffects; set => m_BypassListenerEffects = value; }
        [Tooltip("不将来自 AudioSource 的信号发送到与混响区域关联的全局混响中")]
        [SerializeField] private bool m_BypassReverbZones;
        /// <summary>是否旁通全局混响区。</summary>
        public bool BypassReverbZones { get => m_BypassReverbZones; set => m_BypassReverbZones = value; }

        // 3D 音频设置
        [Header("3D 音频设置 [3D Sound Settings]")]
        [Tooltip("设置音频源应用多普勒效果的程度（如果设置为 0，则不应用任意效果）。")]
        [Range(0f, 5f)]
        [SerializeField] private float m_DopplerLevel;
        /// <summary>多普勒强度（0 无）。</summary>
        public float DopplerLevel { get => m_DopplerLevel; internal set => m_DopplerLevel = value; }
        [Tooltip("设置 3D 立体声或多声道音频在扬声器空间中的传播角度（以度为单位）。")]
        [Range(0, 360)]
        [SerializeField] private int m_Spread;
        /// <summary>扬声器空间传播角（0-360 度）。</summary>
        public int Spread { get => m_Spread; internal set => m_Spread = value; }
        [Tooltip("音频随距离的衰减方式。Logarithmic(对数，现实世界的衰减)、Linear(线性) 和Custom(自定义)。")]
        [SerializeField] private AudioRolloffMode m_RolloffMode;
        /// <summary>距离衰减模式。</summary>
        public AudioRolloffMode RolloffMode { get => m_RolloffMode; internal set => m_RolloffMode = value; }
        [Tooltip("音量停止增大的最小距离")]
        [SerializeField] private float m_MinDistance;
        /// <summary>最小衰减距离。</summary>
        public float MinDistance { get => m_MinDistance; internal set => m_MinDistance = value; }
        [Tooltip("（对数衰减）音频停止衰减的最大距离")]
        [SerializeField] private float m_MaxDistance;
        /// <summary>最大衰减距离。</summary>
        public float MaxDistance { get => m_MaxDistance; internal set => m_MaxDistance = value; }

        [Tooltip("使用自定义音量衰减曲线")]
        [SerializeField] private bool m_UseCustomRolloffCurve;
        /// <summary>是否使用自定义音量衰减曲线。</summary>
        public bool UseCustomRolloffCurve { get => m_UseCustomRolloffCurve; internal set => m_UseCustomRolloffCurve = value; }
        [Tooltip("定义 AudioSource 的音量如何随与 AudioListener 的距离变化而衰减。")]
        [ShowIf(nameof(m_UseCustomRolloffCurve))]
        [SerializeField] private AnimationCurve m_CustomRolloffCurve;
        /// <summary>自定义音量衰减曲线。</summary>
        public AnimationCurve CustomRolloffCurve { get => m_CustomRolloffCurve; internal set => m_CustomRolloffCurve = value; }

        [Tooltip("是否使用自定义空间混合曲线")]
        [SerializeField] private bool m_UseSpatialBlendCurve;
        /// <summary>是否使用自定义空间混合曲线。</summary>
        public bool UseSpatialBlendCurve { get => m_UseSpatialBlendCurve; internal set => m_UseSpatialBlendCurve = value; }
        [Tooltip("如果 UseSpatialBlendCurve 为 true，则用于自定义空间混合的曲线")]
        [ShowIf(nameof(m_UseSpatialBlendCurve))]
        [SerializeField] private AnimationCurve m_SpatialBlendCurve;
        /// <summary>自定义空间混合曲线。</summary>
        public AnimationCurve SpatialBlendCurve { get => m_SpatialBlendCurve; internal set => m_SpatialBlendCurve = value; }

        [Tooltip("是否使用自定义混响区域混音曲线")]
        [SerializeField] private bool m_UseReverbZoneMixCurve;
        /// <summary>是否使用自定义混响区混合曲线。</summary>
        public bool UseReverbZoneMixCurve { get => m_UseReverbZoneMixCurve; internal set => m_UseReverbZoneMixCurve = value; }
        [Tooltip("如果 UseReverbZoneMixCurve 为 true，则用于自定义混响区域混音的曲线")]
        [ShowIf(nameof(m_UseReverbZoneMixCurve))]
        [SerializeField] private AnimationCurve m_ReverbZoneMixCurve;
        /// <summary>自定义混响区混合曲线。</summary>
        public AnimationCurve ReverbZoneMixCurve { get => m_ReverbZoneMixCurve; internal set => m_ReverbZoneMixCurve = value; }

        [Tooltip("是否使用自定义扩散曲线")]
        [SerializeField] private bool m_UseSpreadCurve;
        /// <summary>是否使用自定义扩散曲线。</summary>
        public bool UseSpreadCurve { get => m_UseSpreadCurve; internal set => m_UseSpreadCurve = value; }
        [Tooltip("如果 UseSpreadCurve 为 true，则用于自定义扩散的曲线")]
        [ShowIf(nameof(m_UseSpreadCurve))]
        [SerializeField] private AnimationCurve m_SpreadCurve;
        /// <summary>自定义扩散曲线。</summary>
        public AnimationCurve SpreadCurve { get => m_SpreadCurve; internal set => m_SpreadCurve = value; }

        /// <summary>
        /// 声学默认值（对齐 Unity AudioSource 缺省：多普勒 1 / 最小 1 / 最大 500 / 混响 1 / 对数衰减）。
        /// <para>播放选项的各工厂方法以此为空间起点；直接 <c>new</c> 出来的零值结构体会把声源整形成
        /// 「无多普勒、零衰减距离、无混响」——需要自定义时也请从本默认值覆盖。</para>
        /// </summary>
        public static AudioSpatialOptions Default => new AudioSpatialOptions
        {
            m_PanStereo = 0f,
            m_SpatialBlend = 0f,
            m_ReverbZoneMix = 1f,
            m_BypassEffects = false,
            m_BypassListenerEffects = false,
            m_BypassReverbZones = false,
            m_DopplerLevel = 1f,
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
            m_SpreadCurve = null,
        };
    }
}
