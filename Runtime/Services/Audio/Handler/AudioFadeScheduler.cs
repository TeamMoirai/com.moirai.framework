using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>单条音量过渡状态（紧凑列表存储，零 GC）。</summary>
    internal struct AudioFadeState
    {
        public ulong Handle;
        public float StartTime;
        public float Duration;
        public float StartVolume;
        public float EndVolume;
        public TweenEase Ease;
    }

    /// <summary>
    /// 过渡目标回调——将音量应用到句柄对应目标。
    /// <para>返回 false 表示目标已失效（句柄释放/声部销毁），过渡会被丢弃。</para>
    /// </summary>
    internal interface IAudioFadeTarget
    {
        bool ApplyFade(ulong handle, float volume, bool finished);
    }

    /// <summary>
    /// 音频音量过渡调度器（Unity / 中间件后端共用）。
    /// <para>紧凑列表 + swap-remove，Update 零 GC；声部句柄与总线伪句柄共用一张过渡表。</para>
    /// <para>总线伪句柄占用高位段 0xFFFFFFFF_********，与真实句柄（自 1 递增）无碰撞。</para>
    /// </summary>
    internal sealed class AudioFadeScheduler
    {
        /// <summary>Master 总线过渡伪句柄。</summary>
        public const ulong MASTER_FADE_HANDLE = ulong.MaxValue;

        private const ulong BUS_FLAG_MASK = 0xFFFFFFFF00000000UL;

        private readonly List<AudioFadeState> _fades = new List<AudioFadeState>(8);
        private int _count;

        /// <summary>当前进行中的过渡数量（诊断用）。</summary>
        public int Count => _count;

        /// <summary>音轨总线过渡伪句柄。</summary>
        public static ulong TrackFadeHandle(int trackIndex) => BUS_FLAG_MASK | (uint)trackIndex;

        /// <summary>是否总线（Master/音轨）过渡伪句柄。</summary>
        public static bool IsBusFadeHandle(ulong handle) => (handle & BUS_FLAG_MASK) != 0UL;

        /// <summary>从总线伪句柄解析音轨下标（Master 伪句柄返回 false）。</summary>
        public static bool TryGetBusTrackIndex(ulong handle, out int trackIndex)
        {
            if (handle != MASTER_FADE_HANDLE && (handle & BUS_FLAG_MASK) != 0UL)
            {
                trackIndex = (int)(uint)(handle & 0xFFFFFFFFUL);
                return true;
            }

            trackIndex = -1;
            return false;
        }

        /// <summary>登记一条过渡（同句柄旧过渡应先 <see cref="Stop"/>）。</summary>
        public void Add(in AudioFadeState fade)
        {
            if (_count >= _fades.Count) _fades.Add(default);
            _fades[_count++] = fade;
        }

        /// <summary>停止句柄上的所有过渡。</summary>
        public void Stop(ulong handle)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_fades[i].Handle != handle) continue;
                _count--;
                if (i < _count) _fades[i] = _fades[_count];
            }
        }

        /// <summary>句柄是否有进行中的过渡。</summary>
        public bool IsFading(ulong handle)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_fades[i].Handle == handle) return true;
            }

            return false;
        }

        /// <summary>清空全部过渡（保留列表容量）。</summary>
        public void Clear() => _count = 0;

        /// <summary>
        /// 推进全部过渡：到期写入终值并移除；目标失效（ApplyFade 返回 false）直接丢弃。
        /// </summary>
        public void Update(float now, IAudioFadeTarget target)
        {
            int writeIndex = 0;
            for (int i = 0; i < _count; i++)
            {
                var fade = _fades[i];
                float elapsed = now - fade.StartTime;
                bool finished = elapsed >= fade.Duration;
                float t = fade.Duration > 0f ? Mathf.Min(elapsed / fade.Duration, 1f) : 1f;
                float volume = fade.StartVolume + fade.Ease.Evaluate(t) * (fade.EndVolume - fade.StartVolume);
                if (finished) volume = fade.EndVolume;

                if (!target.ApplyFade(fade.Handle, volume, finished)) continue;
                if (!finished)
                {
                    _fades[writeIndex++] = fade;
                }
            }

            _count = writeIndex;
        }
    }
}
