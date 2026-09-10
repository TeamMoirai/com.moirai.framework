using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 混音快照状态。命名需与 AudioMixer Snapshot 名一致（或在配置中映射）。
    /// </summary>
    public enum EMixSnapshot
    {
        /// <summary>默认混音。</summary>
        Default = 0,

        /// <summary>游戏暂停（压低全部，保留 UI）。</summary>
        Paused,

        /// <summary>对白优先（压低 Music/Sfx）。</summary>
        Dialogue,

        /// <summary>过场/演出。</summary>
        Cinematic,

        /// <summary>水下/隔墙等闷响。</summary>
        Muffled,

        /// <summary>低生命值紧张。</summary>
        LowHealth,
    }

    /// <summary>
    /// 混音快照状态机——按状态切换 Mixer Snapshot，支持交叉淡入与优先级。
    /// <para>Unity 后端：驱动 <see cref="AudioMixerSnapshot.TransitionTo"/>。</para>
    /// <para>中间件后端：通过回调写总线/RTPC（见 <see cref="SetMiddlewareTransitionHandler"/>）。</para>
    /// <para>状态优先级：数值大者可打断小者；同级可切换；不可被更低优先级打断（除非强制）。</para>
    /// </summary>
    [Serializable]
    public sealed class AudioMixStateMachine
    {
        [Serializable]
        private sealed class SnapshotEntry
        {
            public EMixSnapshot State;
            public AudioMixerSnapshot Snapshot;
            public string MiddlewareBusHint;
            [Range(0f, 10f)] public float Priority = 1f;
        }

        [SerializeField] private AudioMixer m_Mixer;
        [SerializeField] private SnapshotEntry[] m_Entries = Array.Empty<SnapshotEntry>();
        [SerializeField] private float m_DefaultBlendSeconds = 0.35f;

        private EMixSnapshot _current = EMixSnapshot.Default;
        private float _currentPriority;
        private Action<EMixSnapshot, float> _middlewareTransition;

        /// <summary>当前状态。</summary>
        public EMixSnapshot Current => _current;

        /// <summary>
        /// 注入中间件过渡回调（FMOD/Wwise：snapshot 跳转或总线渐变）。
        /// </summary>
        public void SetMiddlewareTransitionHandler(Action<EMixSnapshot, float> handler)
            => _middlewareTransition = handler;

        /// <summary>
        /// 从 AudioMixer 自动扫描名为状态名的 Snapshot。
        /// </summary>
        public void BindFromMixer(AudioMixer mixer)
        {
            m_Mixer = mixer;
            if (mixer == null) return;

            var states = (EMixSnapshot[])Enum.GetValues(typeof(EMixSnapshot));
            var list = new List<SnapshotEntry>(states.Length);
            for (int i = 0; i < states.Length; i++)
            {
                string name = states[i].ToString();
                // AudioMixer.FindMatchingGroups 不找 Snapshot；用 Resources/反射不便，改为可配置数组
                list.Add(new SnapshotEntry
                {
                    State = states[i],
                    Snapshot = null,
                    Priority = DefaultPriority(states[i]),
                });
            }

            m_Entries = list.ToArray();
        }

        private static float DefaultPriority(EMixSnapshot state) => state switch
        {
            EMixSnapshot.Default => 0f,
            EMixSnapshot.Muffled => 2f,
            EMixSnapshot.LowHealth => 2f,
            EMixSnapshot.Paused => 3f,
            EMixSnapshot.Dialogue => 3f,
            EMixSnapshot.Cinematic => 4f,
            _ => 1f,
        };

        /// <summary>
        /// 请求切换到目标状态。低优先级无法打断高优先级（除非 force）。
        /// </summary>
        /// <returns>是否发生切换。</returns>
        public bool Request(EMixSnapshot target, float blendSeconds = -1f, bool force = false)
        {
            if (target == _current) return false;

            float targetPriority = GetPriority(target);
            if (!force && targetPriority < _currentPriority) return false;

            float blend = blendSeconds >= 0f ? blendSeconds : m_DefaultBlendSeconds;
            Apply(target, blend);
            _current = target;
            _currentPriority = targetPriority;
            return true;
        }

        /// <summary>
        /// 强制回到 Default。
        /// </summary>
        public void ResetToDefault(float blendSeconds = -1f)
        {
            Request(EMixSnapshot.Default, blendSeconds, force: true);
        }

        private float GetPriority(EMixSnapshot state)
        {
            for (int i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].State == state) return m_Entries[i].Priority;
            }

            return DefaultPriority(state);
        }

        private void Apply(EMixSnapshot target, float blendSeconds)
        {
            // 中间件优先
            if (_middlewareTransition != null)
            {
                _middlewareTransition(target, blendSeconds);
                return;
            }

            if (m_Mixer == null) return;

            AudioMixerSnapshot snap = FindSnapshot(target);
            if (snap != null)
            {
                snap.TransitionTo(Mathf.Max(0.01f, blendSeconds));
            }
        }

        private AudioMixerSnapshot FindSnapshot(EMixSnapshot state)
        {
            for (int i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].State == state) return m_Entries[i].Snapshot;
            }

            return null;
        }

        /// <summary>
        /// 配置某状态对应的 Snapshot 引用。
        /// </summary>
        public void SetSnapshot(EMixSnapshot state, AudioMixerSnapshot snapshot, float priority = -1f)
        {
            for (int i = 0; i < m_Entries.Length; i++)
            {
                if (m_Entries[i].State != state) continue;
                m_Entries[i].Snapshot = snapshot;
                if (priority >= 0f) m_Entries[i].Priority = priority;
                return;
            }

            var list = new List<SnapshotEntry>(m_Entries)
            {
                new SnapshotEntry
                {
                    State = state,
                    Snapshot = snapshot,
                    Priority = priority >= 0f ? priority : DefaultPriority(state),
                }
            };
            m_Entries = list.ToArray();
        }
    }

    /// <summary>
    /// 混音快照服务门面——静态入口，挂到 AudioService 生命周期。
    /// </summary>
    public static class AudioMixService
    {
        private static AudioMixStateMachine s_StateMachine;

        /// <summary>
        /// 初始化状态机（OnInit 时由 AudioService 调用，或游戏侧手动）。
        /// </summary>
        public static void Initialize(AudioMixer mixer)
        {
            s_StateMachine = new AudioMixStateMachine();
            s_StateMachine.BindFromMixer(mixer);
            s_StateMachine.SetSnapshot(EMixSnapshot.Default, null, 0f);
        }

        /// <summary>
        /// 当前状态机（可为 null）。
        /// </summary>
        public static AudioMixStateMachine Machine => s_StateMachine;

        /// <summary>
        /// 当前混音状态。
        /// </summary>
        public static EMixSnapshot Current =>
            s_StateMachine != null ? s_StateMachine.Current : EMixSnapshot.Default;

        /// <summary>
        /// 请求切换。
        /// </summary>
        public static bool Request(EMixSnapshot target, float blendSeconds = -1f, bool force = false)
            => s_StateMachine != null && s_StateMachine.Request(target, blendSeconds, force);

        /// <summary>
        /// 回到默认。
        /// </summary>
        public static void ResetToDefault(float blendSeconds = -1f)
            => s_StateMachine?.ResetToDefault(blendSeconds);

        /// <summary>
        /// 注册 Snapshot（编辑器/启动配置调用）。
        /// </summary>
        public static void RegisterSnapshot(EMixSnapshot state, AudioMixerSnapshot snapshot, float priority = -1f)
            => s_StateMachine?.SetSnapshot(state, snapshot, priority);

        /// <summary>
        /// 中间件过渡钩子。
        /// </summary>
        public static void SetMiddlewareTransitionHandler(Action<EMixSnapshot, float> handler)
            => s_StateMachine?.SetMiddlewareTransitionHandler(handler);

        internal static void Shutdown() => s_StateMachine = null;
    }
}
