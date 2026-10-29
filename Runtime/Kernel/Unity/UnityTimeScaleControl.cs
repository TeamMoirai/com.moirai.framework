using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Kernel.Unity
{
    public sealed class UnityTimeScaleControl : ITimeScaleControl
    {
        public float TimeScale
        {
            get => UnityEngine.Time.timeScale;
            set => UnityEngine.Time.timeScale = value;
        }
    }
}
