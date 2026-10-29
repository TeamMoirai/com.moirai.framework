using UnityEngine;

namespace Moirai.Atropos.Sound
{
    public interface IAudioBackend
    {
        void Play(int voice, AudioClip clip, float volume, bool loop);
        void Stop(int voice, float fadeSeconds);
        void SetVolume(int voice, float volume);
        bool IsPlaying(int voice);
    }
}
