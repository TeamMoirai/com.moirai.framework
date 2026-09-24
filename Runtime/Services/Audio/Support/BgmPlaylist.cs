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

        private const int AutoIdBase = 1_000_000;

        private static readonly System.Collections.Generic.HashSet<int> s_ClaimedIds = new HashSet<int>();
        private static int s_NextAutoId = AutoIdBase;

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
        [Tooltip("分层 ID：0 = 按实例自动分配。显式填相同 ID 会让多个播放列表互相停掉对方的音乐。")]
        [SerializeField] private int m_ID = AutoId;
        [SerializeField, Range(0f, 2f)] private float m_Volume = 1f;
        [SerializeField] internal bool m_PlayOnStart = true;

        [Header("跨曲淡切 [Crossfade]")]
        [SerializeField] private bool m_Crossfade = true;
        [SerializeField, Min(0f)] private float m_CrossfadeSeconds = 1f;

        private readonly ShuffleIndexBag _shuffleBag = new ShuffleIndexBag();

        private int _index = -1;
        internal ulong _handle;
        private bool _playing;
        private int _id;

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
        /// 解析分层 ID：显式值优先，冲突或被占用时改派自动 ID 并报一次；
        /// 0（<see cref="AutoId"/>）走自动分配，避免多个列表默认同 ID 互相 StopByID。
        /// </summary>
        private void ResolveLayerId()
        {
            if (_id != 0)
            {
                s_ClaimedIds.Add(_id);
                return;
            }

            int requested = m_ID;
            if (requested != AutoId)
            {
                if (s_ClaimedIds.Add(requested))
                {
                    _id = requested;
                    return;
                }

                AudioWarnOnce.Warning($"bgm-playlist:id-conflict:{requested}",
                    "[BgmPlaylist] 分层 ID {0} 已被另一个播放列表占用，本实例改用自动分配 ID；否则两者会互相停掉对方的音乐。",
                    requested);
            }

            int auto;
            do
            {
                auto = s_NextAutoId++;
            }
            while (auto > 0 && !s_ClaimedIds.Add(auto));

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
            if (m_PlayOnStart) PlayFromStart();
        }

        private void OnDestroy()
        {
            if (_playing) AudioService.StopByID(_id, 0f);
        }

        /// <summary>从列表开头播放（Shuffle 重新洗一轮）。</summary>
        public void PlayFromStart()
        {
            _index = -1;
            _shuffleBag.Reset();
            PlayNextInternal();
        }

        /// <summary>播放下一首（带跨曲淡切）。</summary>
        public void PlayNext() => PlayNextInternal();

        /// <summary>播放上一首。</summary>
        public void PlayPrevious()
        {
            if (m_Tracks.Count == 0) return;
            int next = _index <= 0 ? m_Tracks.Count - 1 : _index - 1;
            PlayIndex(next);
        }

        /// <summary>播放指定下标。</summary>
        public void PlayIndex(int index)
        {
            if (index < 0 || index >= m_Tracks.Count) return;
            _index = index;
            StartTrack(m_Tracks[_index]);
        }

        /// <summary>停止本层（可淡出）。</summary>
        public void Stop()
        {
            AudioService.StopByID(_id, m_Crossfade ? m_CrossfadeSeconds : 0f);
            _playing = false;
            _handle = 0UL;
        }

        private void PlayNextInternal()
        {
            if (m_Tracks.Count == 0) return;

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
