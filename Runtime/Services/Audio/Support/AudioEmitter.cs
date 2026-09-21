using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 场景音频发射器：挂在关卡物件上播放 2D/3D/跟随音源，支持触发半径与 Gizmos。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Moirai/Audio/Audio Emitter")]
    public sealed class AudioEmitter : MonoBehaviour
    {
        private const int GizmoCircleSegments = 64;
        private const int GizmoLatitudeCount = 3;

        private enum EClipMode
        {
            Address = 0,
            Clip = 1,
        }

        [Header("播放 [Playback]")]
        [SerializeField] private EAudioTrack m_AudioTrack = EAudioTrack.Ambience;
        [SerializeField] private EClipMode m_ClipMode = EClipMode.Clip;
        [SerializeField] private string m_Address = string.Empty;
        [SerializeField] private AudioClip m_Clip;
        [SerializeField] private bool m_PlayOnEnable = true;
        [SerializeField] private bool m_Loop = true;
        [SerializeField, Range(0f, 2f)] private float m_Volume = 1f;
        [SerializeField] private bool m_Async = true;
        [SerializeField] private AudioCachePolicy m_CachePolicy = AudioCachePolicy.Ttl;
        [SerializeField] private bool m_StopWithFadeout = true;
        [SerializeField] private int m_UserId;

        [Header("空间 [Spatial]")]
        [SerializeField] private bool m_FollowSelf = true;
        [SerializeField] private Vector3 m_FollowOffset = Vector3.zero;
        [SerializeField, Range(0f, 1f)] private float m_SpatialBlend = 1f;
        [SerializeField] private AudioRolloffMode m_RolloffMode = AudioRolloffMode.Logarithmic;
        [SerializeField, Min(0f)] private float m_MinDistance = 2f;
        [SerializeField, Min(0f)] private float m_MaxDistance = 30f;

        [Header("触发半径 [Trigger]")]
        [SerializeField] private bool m_UseTriggerRange;
        [SerializeField, Min(0f)] private float m_TriggerRange = 10f;
        [SerializeField, Min(0f)] private float m_TriggerHysteresis = 0.5f;

        [Header("Gizmos")]
        [SerializeField] private bool m_DrawGizmos = true;
        [SerializeField] private bool m_DrawOnlyWhenSelected = true;
        [SerializeField] private Color m_TriggerColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [SerializeField] private Color m_MinDistanceColor = new Color(1f, 0.9f, 0.2f, 0.9f);
        [SerializeField] private Color m_MaxDistanceColor = new Color(1f, 0.45f, 0.05f, 0.9f);

        private Transform _cachedTransform;
        private ulong _handle;
        private bool _insideTriggerRange;

        /// <summary>当前服务句柄（0 = 未在播）。</summary>
        public ulong Handle => _handle;

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying => _handle != 0UL && AudioService.IsPlaying(_handle);

        private void Awake() => _cachedTransform = transform;

        private void OnEnable()
        {
            if (m_PlayOnEnable && !m_UseTriggerRange)
            {
                StartPlayback();
            }
        }

        private void Update()
        {
            if (!m_UseTriggerRange) return;

            bool playing = IsPlaying;
            if (!playing && !_insideTriggerRange && !HasPlayableAsset()) return;

            Vector3 listenerPos = AudioListenerPosition();
            Vector3 offset = listenerPos - _cachedTransform.position;
            float range = playing ? m_TriggerRange + m_TriggerHysteresis : m_TriggerRange;

            if (offset.sqrMagnitude <= range * range)
            {
                if (!_insideTriggerRange || (m_Loop && !playing))
                {
                    _insideTriggerRange = true;
                    StartPlayback();
                }
            }
            else
            {
                _insideTriggerRange = false;
                StopPlayback();
            }
        }

        private void OnDisable()
        {
            StopPlayback();
            _insideTriggerRange = false;
        }

        /// <summary>手动开始播放。</summary>
        public void Play() => StartPlayback();

        /// <summary>手动停止（可淡出）。</summary>
        public void Stop() => StopPlayback();

        private static Vector3 AudioListenerPosition()
        {
            var listener = Object.FindFirstObjectByType<AudioListener>();
            return listener != null ? listener.transform.position : Vector3.zero;
        }

        private void StartPlayback()
        {
            if (IsPlaying || !HasPlayableAsset()) return;

            float maxDistance = m_MaxDistance >= m_MinDistance ? m_MaxDistance : m_MinDistance;

            var options = AudioPlayOptions.Create(m_AudioTrack);
            options.ID = m_UserId != 0 ? m_UserId : GetInstanceID();
            options.Volume = m_Volume;
            options.Loop = m_Loop;
            options.Persistent = true;
            options.DoNotAutoRecycleIfNotDonePlaying = true;
            options.SpatialBlend = m_SpatialBlend;
            options.RolloffMode = m_RolloffMode;
            options.MinDistance = m_MinDistance;
            options.MaxDistance = maxDistance;
            options.CachePolicy = m_CachePolicy;
            options.Location = _cachedTransform.position + m_FollowOffset;
            options.AttachToTransform = m_FollowSelf ? _cachedTransform : null;

            if (m_ClipMode == EClipMode.Clip)
            {
                if (m_Clip == null) return;
                _handle = AudioService.Play(m_Clip, options);
            }
            else
            {
                if (string.IsNullOrEmpty(m_Address)) return;
                _handle = AudioService.Play(m_Address, options, m_Async, false);
            }
        }

        private void StopPlayback()
        {
            if (_handle != 0UL)
            {
                AudioService.Stop(_handle, m_StopWithFadeout ? AudioAgent.FADEOUT_DEFAULT_DURATION : 0f);
            }

            _handle = 0UL;
        }

        private bool HasPlayableAsset()
        {
            return m_ClipMode == EClipMode.Clip ? m_Clip != null : !string.IsNullOrEmpty(m_Address);
        }

        private void OnValidate()
        {
            if (m_MaxDistance < m_MinDistance) m_MaxDistance = m_MinDistance;
        }

        private void OnDrawGizmos()
        {
            if (!m_DrawOnlyWhenSelected) DrawEmitterGizmos();
        }

        private void OnDrawGizmosSelected()
        {
            if (m_DrawOnlyWhenSelected) DrawEmitterGizmos();
        }

        private void DrawEmitterGizmos()
        {
            if (!m_DrawGizmos) return;

            Vector3 position = _cachedTransform != null ? _cachedTransform.position : transform.position;
            if (m_UseTriggerRange) DrawDistanceSphere(position, m_TriggerRange, m_TriggerColor);
            DrawDistanceSphere(position, m_MinDistance, m_MinDistanceColor);
            DrawDistanceSphere(position, m_MaxDistance, m_MaxDistanceColor);
        }

        private static void DrawDistanceSphere(in Vector3 center, float radius, Color color)
        {
            if (radius <= 0f) return;

            Color previous = Gizmos.color;
            Gizmos.color = color;
            DrawCircle(center, Vector3.right, Vector3.forward, radius);
            DrawCircle(center, Vector3.right, Vector3.up, radius);
            DrawCircle(center, Vector3.forward, Vector3.up, radius);

            Color guide = color;
            guide.a *= 0.45f;
            Gizmos.color = guide;
            for (int i = 1; i <= GizmoLatitudeCount; i++)
            {
                float n = i / (float)(GizmoLatitudeCount + 1);
                float angle = n * Mathf.PI * 0.5f;
                float y = Mathf.Sin(angle) * radius;
                float ring = Mathf.Cos(angle) * radius;
                DrawCircle(center + Vector3.up * y, Vector3.right, Vector3.forward, ring);
                DrawCircle(center - Vector3.up * y, Vector3.right, Vector3.forward, ring);
            }

            Gizmos.color = previous;
        }

        private static void DrawCircle(in Vector3 center, in Vector3 axisA, in Vector3 axisB, float radius)
        {
            Vector3 previous = center + axisA * radius;
            for (int i = 1; i <= GizmoCircleSegments; i++)
            {
                float radians = i * (Mathf.PI * 2f / GizmoCircleSegments);
                Vector3 next = center + (axisA * Mathf.Cos(radians) + axisB * Mathf.Sin(radians)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
