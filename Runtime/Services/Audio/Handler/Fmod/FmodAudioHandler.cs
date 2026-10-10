using System;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Fmod
{
    /// <summary>
    /// FMOD 后端 Handler——薄封装，共享 <see cref="MiddlewareAudioHandler"/> 全部生命周期逻辑； <br />
    /// 未定义 <c>FMOD_INSTALLED</c> 时使用 <see cref="FmodBridgeStub"/>。
    /// </summary>
    [ProviderDisplay(title: "FMOD", description: "FMOD 中间件后端（共享中间件生命周期）；未定义 FMOD_INSTALLED 时为桩")]
    [Serializable]
    internal sealed class FmodAudioHandler : MiddlewareAudioHandler
    {
        /// <inheritdoc />
        protected override IAudioMiddlewareBridge CreateDefaultBridge()
        {
#if FMOD_INSTALLED
            return new FmodBridgeNative();
#else
            return new FmodBridgeStub();
#endif
        }
    }
}
