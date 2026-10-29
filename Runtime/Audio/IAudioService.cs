using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Sound
{
    public interface IAudioService
    {
        void SetMasterVolume(float volume);
        bool TryPlay(string clipKey, float volume = 1f, bool loop = false);
        UniTask PlayAsync(string clipKey, float volume, bool loop,
            CancellationToken cancellationToken = default);
        void Stop(int voice, float fadeSeconds);
    }
}
