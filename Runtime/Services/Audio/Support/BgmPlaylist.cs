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
        [SerializeField] private List<AudioClip> m_Tracks = new List<AudioClip>();
        [SerializeField] private EOrderMode m_Order = EOrderMode.Sequential;
        [SerializeField] private ELoopMode m_LoopMode = ELoopMode.LoopList;

        [Header("播放 [Playback]")]
        [SerializeField] private int m_ID = 10001;
        [SerializeField, Range(0f, 2f)] private float m_Volume = 1f;
        [SerializeField] private bool m_PlayOnStart = true;

        [Header("跨曲淡切 [Crossfade]")]
        [SerializeField] private bool m_Crossfade = true;
        [SerializeField, Min(0f)] private float m_CrossfadeSeconds = 1f;

        private int _index = -1;
        private int _lastShuffleIndex = -1;
        private ulong _handle;
        private bool _playing;

        /// <summary>当前曲目下标（-1 表示未开始）。</summary>
        public int CurrentIndex => _index;

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying => _playing && AudioService.IsPlaying(_handle);

        /// <summary>曲目数量。</summary>
        public int TrackCount => m_Tracks.Count;

        private void Start()
        {
            if (m_PlayOnStart) PlayFromStart();
        }

        private void OnDestroy()
        {
            if (_playing) AudioService.StopByID(m_ID, 0f);
        }

        /// <summary>从列表开头播放。</summary>
        public void PlayFromStart()
        {
            _index = -1;
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
            AudioService.StopByID(m_ID, m_Crossfade ? m_CrossfadeSeconds : 0f);
            _playing = false;
            _handle = 0UL;
        }

        private void PlayNextInternal()
        {
            if (m_Tracks.Count == 0) return;

            if (_index < 0)
            {
                _index = m_Order == EOrderMode.Shuffle ? NextShuffleIndex() : 0;
            }
            else if (m_LoopMode == ELoopMode.LoopSingle)
            {
                // 单曲循环：下标不变
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

                _index = m_Order == EOrderMode.Shuffle ? NextShuffleIndex() : next;
            }

            StartTrack(m_Tracks[_index]);
        }

        private int NextShuffleIndex()
        {
            if (m_Tracks.Count <= 1) return 0;

            int candidate;
            int guard = 0;
            do
            {
                candidate = UnityEngine.Random.Range(0, m_Tracks.Count);
                guard++;
            } while (candidate == _lastShuffleIndex && guard < 8);

            _lastShuffleIndex = candidate;
            return candidate;
        }

        private void StartTrack(AudioClip clip)
        {
            if (clip == null) return;

            // 同 ID 交叉：先对旧层淡出，再淡入新层
            if (_playing && m_Crossfade && m_CrossfadeSeconds > 0f)
            {
                AudioService.StopByID(m_ID, m_CrossfadeSeconds);
            }
            else if (_playing)
            {
                AudioService.StopByID(m_ID, 0f);
            }

            bool loopSingle = m_LoopMode == ELoopMode.LoopSingle;
            float fade = m_Crossfade ? m_CrossfadeSeconds : 0f;

            var options = loopSingle
                ? AudioPlayOptions.CreateLooping(EAudioTrack.Music)
                : AudioPlayOptions.CreateWithFade(EAudioTrack.Music, fade, 0f, m_Volume);

            options.ID = m_ID;
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
