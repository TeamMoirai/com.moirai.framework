using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Kernel.Unity
{
    public sealed class UnityClock : IClock
    {
        public float Time => UnityEngine.Time.time;
        public float DeltaTime => UnityEngine.Time.deltaTime;
        public double RealtimeSinceStartup => UnityEngine.Time.realtimeSinceStartupAsDouble;
    }
}
