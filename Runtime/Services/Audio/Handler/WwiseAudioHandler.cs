using System;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Wwise
{
    /// <summary>
    /// Wwise 后端 Handler——薄封装，共享 <see cref="MiddlewareAudioHandler"/>。
    /// <para>未定义 <c>WWISE_INSTALLED</c> 时使用 <see cref="WwiseBridgeStub"/>。</para>
    /// </summary>
    [Serializable]
    public sealed class WwiseAudioHandler : MiddlewareAudioHandler
    {
        /// <inheritdoc />
        protected override IAudioMiddlewareBridge CreateDefaultBridge()
        {
#if WWISE_INSTALLED
            return new WwiseBridgeNative();
#else
            return new WwiseBridgeStub();
#endif
        }
    }
}
