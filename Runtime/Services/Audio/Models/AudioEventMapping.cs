using System;
using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// AudioClip → 中间件事件路径映射项。
    /// <para>配置在中间件后端上（<see cref="Middleware.MiddlewareAudioHandler"/>），让 <c>Play(clip, ...)</c>
    /// 不必依赖「clip 名恰好等于事件名」这条隐性约定——FMOD/Wwise 里事件由音效师命名，clip 只是占位引用。</para>
    /// </summary>
    [Serializable]
    public sealed class AudioEventMapping
    {
        [Tooltip("Unity 侧引用的 AudioClip（中间件后端不加载它，只用作键）")]
        public AudioClip Clip;

        [Tooltip("中间件事件路径：FMOD 形如 event:/Category/Name；Wwise 为事件名")]
        public string EventPath;
    }
}
