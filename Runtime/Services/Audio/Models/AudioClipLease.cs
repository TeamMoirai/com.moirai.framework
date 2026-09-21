using System;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 一条已取得的 clip 租约：<see cref="Clip"/> 供播放使用，<see cref="Release"/> 归还后端引用。
    /// <para>由 <see cref="IAudioClipLeaseSource"/> 产出，缓存只通过它接触资源后端。</para>
    /// </summary>
    internal readonly struct AudioClipLease
    {
        private readonly IDisposable _handle;

        public AudioClipLease(AudioClip clip, IDisposable handle)
        {
            Clip = clip;
            _handle = handle;
        }

        /// <summary>取到的 clip；后端未命中时为 null。</summary>
        public AudioClip Clip { get; }

        /// <summary>是否持有后端租约。</summary>
        public bool IsValid => _handle != null && Clip != null;

        /// <summary>归还后端引用。可重复调用（实现侧幂等）。</summary>
        public void Release()
        {
            _handle?.Dispose();
        }
    }
}
