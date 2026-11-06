using Sirenix.OdinInspector;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 播放时的独奏（Solo）范围：开播时按本档位静音该范围内的其他音频。
    /// </summary>
    public enum EAudioSoloMode : byte
    {
        /// <summary>不独奏。</summary>
        [LabelText("None (不独奏)")]
        None = 0,

        /// <summary>在本音频的目标音轨上独奏：开播时静音该音轨上的所有其他音频。</summary>
        [LabelText("SingleTrack (本音轨)")]
        SingleTrack = 1,

        /// <summary>在所有其他音轨上独奏：开播时静音所有其他音轨。</summary>
        [LabelText("AllTracks (全部音轨)")]
        AllTracks = 2,
    }
}
