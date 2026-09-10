using System;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 遮挡与 HRTF 空间化参数（按声源实例缓存）。
    /// </summary>
    public struct AudioSpatialState
    {
        /// <summary>遮挡系数 0=无遮挡，1=全遮挡。</summary>
        public float Occlusion;

        /// <summary>距离衰减（0–1，由 Listener 距离算出）。</summary>
        public float DistanceAttenuation;

        /// <summary>是否启用 HRTF（耳机空间化）。</summary>
        public bool HrtfEnabled;

        /// <summary>低通截止频率（Hz），遮挡越高越低。</summary>
        public float LowpassHz;
    }

    /// <summary>
    /// 音频遮挡与 HRTF 管理器——对激活声源做射线遮挡检测，并驱动低通/空间化。
    /// <para>挂在 AudioListener 所在物体或全局服务根；每帧以可配置间隔轮询。</para>
    /// <para>Unity 后端：改 <see cref="AudioLowPassFilter"/> 截止；中间件后端：写 RTPC/LPF。</para>
    /// </summary>
    [AddComponentMenu("Moirai/Audio/Audio Occlusion HRTF")]
    public sealed class AudioOcclusionHrtf : MonoBehaviour
    {
        [Header("检测 [Detection]")]
        [Tooltip("射线检测层（遮挡物所在层）")]
        [SerializeField] private LayerMask m_OcclusionMask = ~0;

        [Tooltip("检测间隔（秒）。0 = 每帧")]
        [Range(0f, 0.5f)]
        [SerializeField] private float m_Interval = 0.08f;

        [Tooltip("最大同时检测声源数")]
        [SerializeField] private int m_MaxSources = 24;

        [Tooltip("全遮挡时低通截止（Hz）")]
        [SerializeField] private float m_MinLowpassHz = 400f;

        [Tooltip("无遮挡时低通截止（Hz）")]
        [SerializeField] private float m_MaxLowpassHz = 22000f;

        [Tooltip("遮挡插值速度")]
        [SerializeField] private float m_SmoothSpeed = 8f;

        [Header("HRTF")]
        [Tooltip("启用 HRTF（3D spatialBlend 推到 1，Unity 用内置空间化；中间件写 HRTF 开关）")]
        [SerializeField] private bool m_HrtfEnabled = true;

        private Transform _listener;
        private float _nextTick;
        private readonly System.Collections.Generic.List<AudioSource> _sources =
            new System.Collections.Generic.List<AudioSource>(32);
        private readonly System.Collections.Generic.Dictionary<AudioSource, float> _occlusionSmooth =
            new System.Collections.Generic.Dictionary<AudioSource, float>(32);
        private readonly System.Collections.Generic.Dictionary<AudioSource, AudioLowPassFilter> _filters =
            new System.Collections.Generic.Dictionary<AudioSource, AudioLowPassFilter>(32);

        private void Awake()
        {
            var listener = FindFirstObjectByType<AudioListener>();
            _listener = listener != null ? listener.transform : transform;
        }

        private void OnEnable()
        {
            AudioOcclusionRegistry.Register(this);
        }

        private void OnDisable()
        {
            AudioOcclusionRegistry.Unregister(this);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + Mathf.Max(0f, m_Interval);
            TickOcclusion();
        }

        private void TickOcclusion()
        {
            if (_listener == null) return;

            CollectSources();
            Vector3 listenerPos = _listener.position;
            int count = Mathf.Min(_sources.Count, m_MaxSources);

            for (int i = 0; i < count; i++)
            {
                var source = _sources[i];
                if (source == null) continue;

                Vector3 sourcePos = source.transform.position;
                float raw = ComputeOcclusion(listenerPos, sourcePos);

                float current = _occlusionSmooth.TryGetValue(source, out var s) ? s : 0f;
                float smoothed = Mathf.Lerp(current, raw, 1f - Mathf.Exp(-m_SmoothSpeed * Time.unscaledDeltaTime));
                _occlusionSmooth[source] = smoothed;

                ApplyToSource(source, smoothed);
            }
        }

        private void CollectSources()
        {
            _sources.Clear();
            var categories = AudioService.AudioCategories;
            if (categories == null) return;

            for (int i = 0; i < categories.Length; i++)
            {
                var agents = categories[i]?.AudioAgents;
                if (agents == null) continue;
                for (int j = 0; j < agents.Count; j++)
                {
                    var agent = agents[j];
                    var src = agent?.AudioResource;
                    if (src != null && src.isPlaying) _sources.Add(src);
                }
            }
        }

        /// <summary>
        /// 单次射线遮挡：0 通透，1 全挡。
        /// </summary>
        private float ComputeOcclusion(Vector3 listenerPos, Vector3 sourcePos)
        {
            Vector3 dir = sourcePos - listenerPos;
            float dist = dir.magnitude;
            if (dist < 0.01f) return 0f;

            dir /= dist;
            if (!Physics.Raycast(listenerPos, dir, out var hit, dist, m_OcclusionMask, QueryTriggerInteraction.Ignore))
            {
                return 0f;
            }

            // 命中点接近声源视为通透（声源在障碍物前）
            float toSource = dist;
            float toHit = hit.distance;
            if (toHit >= toSource - 0.15f) return 0f;

            return 1f;
        }

        private void ApplyToSource(AudioSource source, float occlusion)
        {
            float lowpass = Mathf.Lerp(m_MaxLowpassHz, m_MinLowpassHz, occlusion);

            if (!_filters.TryGetValue(source, out var filter) || filter == null)
            {
                filter = source.GetComponent<AudioLowPassFilter>();
                if (filter == null) filter = source.gameObject.AddComponent<AudioLowPassFilter>();
                _filters[source] = filter;
            }

            filter.cutoffFrequency = lowpass;

            if (m_HrtfEnabled)
            {
                // 3D 声源推满 spatialBlend，配合 Unity 空间化/HRTF 插件
                if (source.spatialBlend < 1f)
                {
                    source.spatialBlend = Mathf.MoveTowards(source.spatialBlend, 1f, Time.unscaledDeltaTime * 4f);
                }
            }
        }

        /// <summary>
        /// 当前声源遮挡状态（诊断/中间件驱动）。
        /// </summary>
        public bool TryGetState(AudioSource source, out AudioSpatialState state)
        {
            state = default;
            if (source == null) return false;
            if (!_occlusionSmooth.TryGetValue(source, out var occ)) return false;

            state.Occlusion = occ;
            state.HrtfEnabled = m_HrtfEnabled;
            state.LowpassHz = Mathf.Lerp(m_MaxLowpassHz, m_MinLowpassHz, occ);
            return true;
        }
    }

    /// <summary>
    /// 遮挡管理器注册表（静态访问，供 Service / 中间件桥查询）。
    /// </summary>
    public static class AudioOcclusionRegistry
    {
        private static AudioOcclusionHrtf s_Active;

        public static AudioOcclusionHrtf Active => s_Active;

        public static void Register(AudioOcclusionHrtf handler) => s_Active = handler;

        public static void Unregister(AudioOcclusionHrtf handler)
        {
            if (s_Active == handler) s_Active = null;
        }
    }
}
