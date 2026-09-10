using System;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Fmod
{
    /// <summary>
    /// FMOD 后端 Handler——薄封装，共享 <see cref="MiddlewareAudioHandler"/> 全部生命周期逻辑。
    /// <para>未定义 <c>FMOD_INSTALLED</c> 时使用 <see cref="FmodBridgeStub"/>。</para>
    /// </summary>
    [Serializable]
    public sealed class FmodAudioHandler : MiddlewareAudioHandler
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
