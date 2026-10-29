namespace Moirai.Atropos.Storage
{
    public readonly struct StorageCommitResult
    {
        public StorageCommitResult(bool powerLossDurable) { PowerLossDurable = powerLossDurable; }
        public bool PowerLossDurable { get; }
    }
}
