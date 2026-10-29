namespace Moirai.Atropos.Bootstrap
{
    public enum BootPhase : byte
    {
        NotStarted,
        Building,
        Initializing,
        StartingProcedures,
        Ready,
        Failed,
        Stopping,
        Stopped
    }
}
