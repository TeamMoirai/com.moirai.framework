using UnityEngine;

namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 可选桥接能力：显式加载/卸载声音库（FMOD Studio bank / Wwise SoundBank）。
    /// </summary>
    /// <remarks>不做成 <see cref="IAudioMiddlewareBridge"/> 的成员，否则未实现它的桥会编译不过；能力探测走 <c>as</c>，冷路径一次。</remarks>
    internal interface IAudioMiddlewareBankControl
    {
        /// <summary>
        /// 加载声音库，返回三态 <see cref="EAudioBankLoadResult"/>（只有 <see cref="EAudioBankLoadResult.Failed"/> 才被上层告警）。
        /// </summary>
        EAudioBankLoadResult LoadBank(string bankPath);

        /// <summary>
        /// 卸载声音库；未加载或失败返回 false。
        /// </summary>
        bool UnloadBank(string bankPath);
    }
}
