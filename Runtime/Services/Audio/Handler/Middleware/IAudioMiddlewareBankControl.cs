using UnityEngine;

namespace Moirai.Atropos.Audio.Middleware
{
    /// <summary>
    /// 可选桥接能力：显式加载/卸载声音库（FMOD Studio bank / Wwise SoundBank）。
    /// <para>刻意不做成 <see cref="IAudioMiddlewareBridge"/> 的成员：那会让未实现它的桥（含真 SDK 桥）
    /// 在定义 <c>FMOD_INSTALLED</c> / <c>WWISE_INSTALLED</c> 时直接编译不过。能力探测走 <c>as</c>，冷路径一次。</para>
    /// </summary>
    internal interface IAudioMiddlewareBankControl
    {
        /// <summary>加载声音库；已加载或失败返回 false。</summary>
        bool LoadBank(string bankPath);

        /// <summary>卸载声音库；未加载或失败返回 false。</summary>
        bool UnloadBank(string bankPath);
    }
}
