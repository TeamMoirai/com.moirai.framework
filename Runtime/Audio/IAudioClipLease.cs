using System;
using UnityEngine;

namespace Moirai.Atropos.Sound
{
    public interface IAudioClipLease : IDisposable
    {
        AudioClip Clip { get; }
    }
}
