using System;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Wwise
{
    /// <summary>
    /// Wwise 后端 Handler——薄封装，共享 <see cref="MiddlewareAudioHandler"/>； <br />
    /// 未定义 <c>WWISE_INSTALLED</c> 时使用 <see cref="WwiseBridgeStub"/>。
    /// </summary>
    [ProviderDisplay(title: "Wwise", description: "Wwise 中间件后端（共享中间件生命周期）；未定义 WWISE_INSTALLED 时为桩")]
    [Serializable]
    internal sealed class WwiseAudioHandler : MiddlewareAudioHandler
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
