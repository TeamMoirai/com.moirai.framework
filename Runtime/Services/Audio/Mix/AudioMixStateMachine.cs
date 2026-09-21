using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// 混音快照配置项——状态 → AudioMixerSnapshot 映射（<see cref="AudioServiceSettings"/> 序列化用）。
    /// </summary>
    [Serializable]
    public sealed class AudioMixSnapshotEntry
    {
        /// <summary>混音快照状态。</summary>
        public EMixSnapshot State;

        /// <summary>对应的 AudioMixer 快照。</summary>
        public AudioMixerSnapshot Snapshot;

        /// <summary>打断优先级；&lt;0 表示使用内置默认优先级。</summary>
        [Range(-1f, 10f)] public float Priority = -1f;
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
        /// 从 AudioMixer 自动扫描名为状态名的 Snapshot（仅铺出空条目；引用请用 <see cref="TryBindSnapshotsByName"/>）。
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
                // AudioMixer.FindMatchingGroups 不找 Snapshot；引用经 TryBindSnapshotsByName 反射补齐
                list.Add(new SnapshotEntry
                {
                    State = states[i],
                    Snapshot = null,
                    Priority = DefaultPriority(states[i]),
                });
            }

            m_Entries = list.ToArray();
        }

        /// <summary>
        /// 在 Snapshot 名列表中按状态名求索引（纯逻辑，便于单测）。
        /// <para>精确序数匹配优先；未命中再忽略大小写。未命中返回 -1。</para>
        /// </summary>
        internal static int ResolveSnapshotIndex(string[] snapshotNames, EMixSnapshot state)
        {
            if (snapshotNames == null || snapshotNames.Length == 0) return -1;

            string name = state.ToString();
            int ignoreCase = -1;
            for (int i = 0; i < snapshotNames.Length; i++)
            {
                string candidate = snapshotNames[i];
                if (string.IsNullOrEmpty(candidate)) continue;
                if (string.Equals(candidate, name, StringComparison.Ordinal)) return i;
                if (ignoreCase < 0 && string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                {
                    ignoreCase = i;
                }
            }

            return ignoreCase;
        }

        /// <summary>
        /// 读取 AudioMixer 内全部 Snapshot（反射 <c>m_Snapshots</c>；编辑器下 SerializedObject 回退）。
        /// </summary>
        internal static AudioMixerSnapshot[] CollectMixerSnapshots(AudioMixer mixer)
        {
            if (mixer == null) return Array.Empty<AudioMixerSnapshot>();

            var field = typeof(AudioMixer).GetField("m_Snapshots",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(mixer) is AudioMixerSnapshot[] reflected && reflected.Length > 0)
            {
                return reflected;
            }

#if UNITY_EDITOR
            var so = new UnityEditor.SerializedObject(mixer);
            var prop = so.FindProperty("m_Snapshots");
            if (prop != null && prop.isArray && prop.arraySize > 0)
            {
                var list = new List<AudioMixerSnapshot>(prop.arraySize);
                for (int i = 0; i < prop.arraySize; i++)
                {
                    if (prop.GetArrayElementAtIndex(i).objectReferenceValue is AudioMixerSnapshot snap)
                    {
                        list.Add(snap);
                    }
                }

                if (list.Count > 0) return list.ToArray();
            }
#endif

            return Array.Empty<AudioMixerSnapshot>();
        }

        /// <summary>
        /// 按 Snapshot 名与 <see cref="EMixSnapshot"/> 自动绑定（一键绑定）。
        /// <para>仅填充当前 <see cref="SnapshotEntry.Snapshot"/> 为空的条目——Settings 手工映射始终优先。</para>
        /// </summary>
        /// <returns>本次成功绑定的数量。</returns>
        public int TryBindSnapshotsByName(AudioMixer mixer)
        {
            if (mixer == null) return 0;
            m_Mixer = mixer;

            var snapshots = CollectMixerSnapshots(mixer);
            if (snapshots.Length == 0)
            {
                WarnUnboundAfterAutoBind();
                return 0;
            }

            var names = new string[snapshots.Length];
            for (int i = 0; i < snapshots.Length; i++)
            {
                names[i] = snapshots[i] != null ? snapshots[i].name : null;
            }

            var states = (EMixSnapshot[])Enum.GetValues(typeof(EMixSnapshot));
            int bound = 0;
            for (int s = 0; s < states.Length; s++)
            {
                var state = states[s];
                // 手工映射（Settings 非空 Snapshot）优先，不覆盖
                if (FindSnapshot(state) != null) continue;

                int index = ResolveSnapshotIndex(names, state);
                if (index < 0 || snapshots[index] == null) continue;

                SetSnapshot(state, snapshots[index]);
                bound++;
            }

            WarnUnboundAfterAutoBind();
            return bound;
        }

        /// <summary>
        /// 自动绑定后仍无 Snapshot 的状态告警一次（Default 例外：回 Mixer 默认态属正常）。
        /// </summary>
        private void WarnUnboundAfterAutoBind()
        {
            var states = (EMixSnapshot[])Enum.GetValues(typeof(EMixSnapshot));
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state == EMixSnapshot.Default) continue;
                if (FindSnapshot(state) != null) continue;

                AudioWarnOnce.Warning($"mix.auto-bind-missing:{state}",
                    "[AudioMix] 自动绑定后状态 {0} 仍无 AudioMixerSnapshot（Mixer 内需有同名 Snapshot，或在 AudioServiceSettings.MixSnapshots 手工映射）。",
                    state);
            }
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
        /// <returns>
        /// <c>true</c> 当且仅当过渡**真的施加到了混音上**（Mixer 快照或中间件回调）。
        /// <para>没施加就不改 <see cref="Current"/>：否则一个从未生效的状态会一直挡着后续低优先级请求，
        /// 并让自动 Ducking 误记"这层混音是我借走的"。</para>
        /// </returns>
        public bool Request(EMixSnapshot target, float blendSeconds = -1f, bool force = false)
        {
            if (target == _current) return false;

            float targetPriority = GetPriority(target);
            if (!force && targetPriority < _currentPriority) return false;

            float blend = blendSeconds >= 0f ? blendSeconds : m_DefaultBlendSeconds;
            if (!TryApply(target, blend)) return false;

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

        /// <summary>
        /// 把目标状态真的施加到混音上：中间件回调优先，其次 Mixer 快照。
        /// </summary>
        /// <returns>真的施加了返回 true；没有任何可施加的对象时返回 false（调用方不得改状态记账）。</returns>
        private bool TryApply(EMixSnapshot target, float blendSeconds)
        {
            // 中间件优先
            if (_middlewareTransition != null)
            {
                _middlewareTransition(target, blendSeconds);
                return true;
            }

            if (m_Mixer == null)
            {
                // 自动 Ducking 会按台词反复请求，这里必须按状态去重，否则每次进对白都刷一条
                AudioWarnOnce.Warning($"mix.no-mixer:{target}",
                    "[AudioMix] Mixer 未绑定且无中间件过渡回调，状态 {0} 无法施加，切换被拒绝。", target);
                return false;
            }

            AudioMixerSnapshot snap = FindSnapshot(target);
            if (snap != null)
            {
                snap.TransitionTo(Mathf.Max(0.01f, blendSeconds));
                return true;
            }

            if (target != EMixSnapshot.Default)
            {
                // 状态缺失视为配置遗漏，按状态报一次
                AudioWarnOnce.Warning($"mix.no-snapshot:{target}",
                    "[AudioMix] 状态 {0} 未注册 AudioMixerSnapshot（见 AudioServiceSettings.MixSnapshots 或按名自动绑定），切换被拒绝。",
                    target);
                return false;
            }

            // 回 Default 在 Unity 里同样需要一个可 TransitionTo 的快照；缺它就等于回不去，
            // 必须显式报出来——否则 Ducking 压低混音后再也抬不回来，且没人知道为什么。
            AudioWarnOnce.Warning("mix.no-snapshot:Default",
                "[AudioMix] 无 Default 快照可回落：请在 Mixer 里建一个名为 Default 的 Snapshot（会被按名自动绑定），" +
                "或在 AudioServiceSettings.MixSnapshots 显式登记。");
            return false;
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
        /// <para>顺序：铺空条目 → Settings 手工映射优先写入 → 空缺按名自动绑定；已配置的条目一律不被覆盖。</para>
        /// </summary>
        public static void Initialize(AudioMixer mixer)
        {
            s_StateMachine = new AudioMixStateMachine();
            s_StateMachine.BindFromMixer(mixer);

            // Settings 非空 Snapshot 先落地（手工映射 = 覆盖）；无配置资产时跳过
            var entries = AudioServiceSettings.MixSnapshots;
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (entry == null || entry.Snapshot == null) continue;
                    s_StateMachine.SetSnapshot(entry.State, entry.Snapshot, entry.Priority);
                }
            }

            // Settings 未覆盖的空条目按名自动绑定（含 Default：Mixer 里名为 Default 的快照会被绑上，
            // 那是"回落"唯一真正可施加的目标）。这里不得再无条件清空 Default——那会抹掉作者刚配好的行。
            s_StateMachine.TryBindSnapshotsByName(mixer);
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
