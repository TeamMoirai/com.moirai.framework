namespace Moirai.Atropos.Kernel.Scheduling
{
    public interface IClock
    {
        float Time { get; }
        float DeltaTime { get; }
        double RealtimeSinceStartup { get; }
    }
}
