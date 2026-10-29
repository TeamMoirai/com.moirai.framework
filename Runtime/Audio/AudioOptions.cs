using System;

namespace Moirai.Atropos.Sound
{
    public sealed class AudioOptions
    {
        public int VoiceCapacity { get; set; } = 8;
        public float DefaultFadeSeconds { get; set; } = 0.2f;
        public float MasterVolume { get; set; } = 1f;

        internal void Validate()
        {
            if (VoiceCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(VoiceCapacity));
            if (DefaultFadeSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(DefaultFadeSeconds));
            if (MasterVolume < 0f || MasterVolume > 1f) throw new ArgumentOutOfRangeException(nameof(MasterVolume));
        }
    }
}
