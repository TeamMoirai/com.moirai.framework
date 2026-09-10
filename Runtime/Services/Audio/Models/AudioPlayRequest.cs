using System;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 播放热路径标志位（打包进 <see cref="AudioPlayRequest"/>）。
    /// </summary>
    [Flags]
    public enum AudioPlayFlags : byte
    {
        None = 0,
        /// <summary>循环播放</summary>
        Loop = 1 << 0,
        /// <summary>跨场景持久</summary>
        Persistent = 1 << 1,
        /// <summary>播放时淡入</summary>
        FadeInOnPlay = 1 << 2,
        /// <summary>单轨 Solo</summary>
        SoloSingleTrack = 1 << 3,
        /// <summary>全轨 Solo</summary>
        SoloAllTracks = 1 << 4,
        /// <summary>结束自动取消 Solo</summary>
        AutoUnSoloOnEnd = 1 << 5,
        /// <summary>不抢占未播完的通道</summary>
        DoNotAutoRecycle = 1 << 6,
    }

    /// <summary>
    /// 播放热路径请求——固定 16 字节，按值拷贝零堆分配。
    /// <para>布局：Id(4) + Volume(4) + Pitch(4) + Packed(4)。</para>
    /// <para>Packed = Track:8 | Priority:8 | Flags:8 | pad:8。</para>
    /// <para>位置/曲线/旁通等冷参数见 <see cref="AudioPlayColdParams"/>。</para>
    /// </summary>
    public readonly struct AudioPlayRequest
    {
        /// <summary>用户定义 ID。</summary>
        public readonly int Id;

        /// <summary>音量 0–2。</summary>
        public readonly float Volume;

        /// <summary>音调 -3–3。</summary>
        public readonly float Pitch;

        private readonly uint _packed;

        public AudioPlayRequest(int id, float volume, float pitch, EAudioTrack track, byte priority, AudioPlayFlags flags)
        {
            Id = id;
            Volume = volume;
            Pitch = pitch;
            _packed = (uint)(byte)track
                      | ((uint)priority << 8)
                      | ((uint)(byte)flags << 16);
        }

        /// <summary>音轨。</summary>
        public EAudioTrack Track => (EAudioTrack)(_packed & 0xFFu);

        /// <summary>优先级（0 最高，255 最低）。</summary>
        public byte Priority => (byte)((_packed >> 8) & 0xFFu);

        /// <summary>标志位。</summary>
        public AudioPlayFlags Flags => (AudioPlayFlags)((_packed >> 16) & 0xFFu);

        public bool Loop => (Flags & AudioPlayFlags.Loop) != 0;
        public bool Persistent => (Flags & AudioPlayFlags.Persistent) != 0;
        public bool FadeInOnPlay => (Flags & AudioPlayFlags.FadeInOnPlay) != 0;
        public bool SoloSingleTrack => (Flags & AudioPlayFlags.SoloSingleTrack) != 0;
        public bool SoloAllTracks => (Flags & AudioPlayFlags.SoloAllTracks) != 0;
        public bool AutoUnSoloOnEnd => (Flags & AudioPlayFlags.AutoUnSoloOnEnd) != 0;
        public bool DoNotAutoRecycleIfNotDonePlaying => (Flags & AudioPlayFlags.DoNotAutoRecycle) != 0;

        /// <summary>
        /// 默认 Sfx 请求（音量 1、音调 1、不抢占）。
        /// </summary>
        public static AudioPlayRequest Default =>
            new AudioPlayRequest(0, 1f, 1f, EAudioTrack.Sfx, 128, AudioPlayFlags.DoNotAutoRecycle);
    }
}
