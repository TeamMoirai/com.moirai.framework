using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// BGM 播放列表：顺序/随机 + 跨曲淡切 + 分层 ID。
    /// <para>切歌用 <see cref="AudioService.StopByID"/> + Fade 交叉过渡，
    /// 同层替换不打断其它 Music 分层。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Moirai/Audio/BGM Playlist")]
    public sealed class BgmPlaylist : MonoBehaviour
    {
        /// <summary>分层 ID 留空（0）时按实例自动分配，避免多个播放列表默认同 ID 互相停掉。</summary>
        public const int AutoId = 0;

        internal static readonly System.Collections.Generic.HashSet<int> s_ClaimedIds = new HashSet<int>();

        // 自动分配走负区间（-1 起递减），与显式正数 ID 值域不相交；
        // 理论上递减到 int.MinValue 后会回绕，实际到不了，不另加溢出防护。
        // s_* 与下列成员走 internal 是测试接缝（《测试规范》：测试禁反射，需触达的成员放宽 internal）
        internal static int s_NextAutoId = -1;

        /// <summary>
        /// Enter Play Mode Options 关闭域重载时静态跨局残留：进玩前清一次注册表与游标。
        /// 正常路径仍靠 OnDisable 归还；这里是崩溃/强杀后的兜底，不是主清理通道。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticIdRegistry()
        {
            s_ClaimedIds.Clear();
            s_NextAutoId = -1;
        }

        public enum ELoopMode
        {
            /// <summary>播完列表停在最后一首。</summary>
            None = 0,

            /// <summary>列表循环。</summary>
            LoopList = 1,

            /// <summary>单曲循环。</summary>
            LoopSingle = 2,
        }

        public enum EOrderMode
        {
            Sequential = 0,
            Shuffle = 1,
        }

        [Header("曲目 [Playlist]")]
        [SerializeField] internal List<AudioClip> m_Tracks = new List<AudioClip>();
        [SerializeField] private EOrderMode m_Order = EOrderMode.Sequential;
        [SerializeField] private ELoopMode m_LoopMode = ELoopMode.LoopList;

        [Header("播放 [Playback]")]
        [Tooltip("分层 ID：正数 = 显式分层（与其它播放列表撞车会报错且本列表不播放）；0 = 按实例自动分配（负区间为自动分配保留）。")]
        [SerializeField] internal int m_ID = AutoId;
        [SerializeField, Range(0f, 2f)] private float m_Volume = 1f;
        [SerializeField] internal bool m_PlayOnStart = true;

        [Header("跨曲淡切 [Crossfade]")]
        [SerializeField] private bool m_Crossfade = true;
        [SerializeField, Min(0f)] private float m_CrossfadeSeconds = 1f;

        private readonly ShuffleIndexBag _shuffleBag = new ShuffleIndexBag();

        private int _index = -1;
        internal ulong _handle;
        private bool _playing;
        internal int _id;
        internal bool _layerConflicted;

        /// <summary>本列表实际占用的分层 ID（自动分配时为运行期才确定）。</summary>
        public int LayerId => _id;

        /// <summary>当前曲目下标（-1 表示未开始）。</summary>
        public int CurrentIndex => _index;

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying => _playing && AudioService.IsPlaying(_handle);

        /// <summary>曲目数量。</summary>
        public int TrackCount => m_Tracks.Count;

        private void OnEnable()
        {
            ResolveLayerId();
        }

        private void OnDisable()
        {
            Stop();
            ReleaseLayerId();
        }

        /// <summary>
        /// 解析分层 ID：显式正数优先（撞车 fail-fast，本实例不启动播放）；
        /// 0（<see cref="AutoId"/>）走自动分配（负区间），避免多个列表默认同 ID 互相 StopByID。
        /// <para>internal 为测试接缝（EditMode 无法自动执行 OnEnable）。</para>
        /// </summary>
        internal void ResolveLayerId()
        {
            _layerConflicted = false;

            if (_id != 0)
            {
                s_ClaimedIds.Add(_id);
                return;
            }

            int requested = m_ID;
            if (requested != AutoId)
            {
                if (requested < 0)
                {
                    LogUtility.Error(
                        "[BgmPlaylist] 分层 ID {0} 无效：显式 ID 必须为正数（0 = 自动分配，负数区间为自动分配保留）。本实例不会启动播放。",
                        requested);
                    _layerConflicted = true;
                    return;
                }

                if (s_ClaimedIds.Add(requested))
                {
                    _id = requested;
                    return;
                }

                // fail-fast：撞 ID 不再静默改派——两个列表互相 StopByID 是线上事故，必须在开发期暴露
                LogUtility.Error(
                    "[BgmPlaylist] 分层 ID {0} 已被另一个播放列表占用。本实例不会启动播放；请改用不同的正数 ID 或 0（自动分配）。",
                    requested);
                _layerConflicted = true;
                return;
            }

            int auto;
            do
            {
                auto = s_NextAutoId--;
            }
            while (!s_ClaimedIds.Add(auto));

            _id = auto;
        }

        private void ReleaseLayerId()
        {
            if (_id == 0) return;
            s_ClaimedIds.Remove(_id);
            _id = 0;
        }

        private void Start()
        {
            if (m_PlayOnStart && !_layerConflicted) PlayFromStart();
        }

        private void OnDestroy()
        {
            if (_playing) AudioService.StopByID(_id, 0f);
        }

        /// <summary>从列表开头播放（Shuffle 重新洗一轮）。</summary>
        public void PlayFromStart()
        {
            if (_layerConflicted) return;
            _index = -1;
            _shuffleBag.Reset();
            PlayNextInternal();
        }

        /// <summary>播放下一首（带跨曲淡切）。</summary>
        public void PlayNext() => PlayNextInternal();

        /// <summary>播放上一首。</summary>
        public void PlayPrevious()
        {
            if (_layerConflicted || m_Tracks.Count == 0) return;
            int next = _index <= 0 ? m_Tracks.Count - 1 : _index - 1;
            PlayIndex(next);
        }

        /// <summary>播放指定下标。</summary>
        public void PlayIndex(int index)
        {
            if (_layerConflicted) return;
            if (index < 0 || index >= m_Tracks.Count) return;
            _index = index;
            StartTrack(m_Tracks[_index]);
        }

        /// <summary>停止本层（可淡出）。</summary>
        public void Stop()
        {
            // 冲突/未解析实例不占层；0 是「未指定 ID」的默认音组，误停会波及所有无 ID 播放
            if (_id == 0) return;
            AudioService.StopByID(_id, m_Crossfade ? m_CrossfadeSeconds : 0f);
            _playing = false;
            _handle = 0UL;
        }

        private void PlayNextInternal()
        {
            if (_layerConflicted || m_Tracks.Count == 0) return;

            if (_index < 0)
            {
                _index = m_Order == EOrderMode.Shuffle ? _shuffleBag.Next(m_Tracks.Count) : 0;
            }
            else if (m_LoopMode == ELoopMode.LoopSingle)
            {
                // 单曲循环：下标不变
            }
            else if (m_Order == EOrderMode.Shuffle)
            {
                // Shuffle 不走顺序下标回绕（否则随机落到末位会只播一首就收尾）：袋空即本轮播完
                if (_shuffleBag.Remaining == 0 && m_LoopMode == ELoopMode.None)
                {
                    Stop();
                    return;
                }

                _index = _shuffleBag.Next(m_Tracks.Count);
            }
            else
            {
                int next = _index + 1;
                if (next >= m_Tracks.Count)
                {
                    if (m_LoopMode != ELoopMode.LoopList)
                    {
                        Stop();
                        return;
                    }

                    next = 0;
                }

                _index = next;
            }

            StartTrack(m_Tracks[_index]);
        }

        private void StartTrack(AudioClip clip)
        {
            // 分层 ID 撞车后本实例不再启动播放（fail-fast 已在 ResolveLayerId 报错）
            if (_layerConflicted) return;

            // 空槽位是配置错误：报一次再收口。旧行为是静默停下（_playing 由 Update 兜着复位），
            // 症状为"播到某首就没声音了"且无任何提示。
            if (clip == null)
            {
                AudioWarnOnce.Warning($"bgm-playlist:null-track:{GetInstanceID()}",
                    "[BgmPlaylist] 下标 {0} 的曲目为空，播放列表停止。", _index);
                Stop();
                return;
            }

            // 同 ID 交叉：先对旧层淡出，再淡入新层
            if (_playing && m_Crossfade && m_CrossfadeSeconds > 0f)
            {
                AudioService.StopByID(_id, m_CrossfadeSeconds);
            }
            else if (_playing)
            {
                AudioService.StopByID(_id, 0f);
            }

            bool loopSingle = m_LoopMode == ELoopMode.LoopSingle;
            float fade = m_Crossfade ? m_CrossfadeSeconds : 0f;

            var options = loopSingle
                ? AudioPlayOptions.CreateLooping(EAudioTrack.Music)
                : AudioPlayOptions.CreateWithFade(EAudioTrack.Music, fade, 0f, m_Volume);

            options.ID = _id;
            options.Loop = loopSingle;
            options.Persistent = true;
            options.Volume = m_Volume;
            options.DoNotAutoRecycleIfNotDonePlaying = true;
            if (!m_Crossfade)
            {
                options.FadeInOnPlay = false;
            }

            _handle = AudioService.Play(clip, options);
            _playing = _handle != 0UL;

            if (!_playing)
            {
                // 取不到通道/后端未就绪时旧行为是静默停止推进，表现为"BGM 莫名没了"
                AudioWarnOnce.Warning($"bgm-playlist:no-voice:{GetInstanceID()}",
                    "[BgmPlaylist] 曲目 {0} 播放失败（无可用通道、音轨暂停或后端未初始化），列表停止推进。", _index);
            }
        }

        private void Update()
        {
            if (!_playing || m_LoopMode == ELoopMode.LoopSingle) return;

            // 单曲（非 LoopSingle）自然播完 → 进下一首 / 列表收尾
            if (!AudioService.IsPlaying(_handle))
            {
                _playing = false;
                _handle = 0UL;
                PlayNextInternal();
            }
        }
    }
}
