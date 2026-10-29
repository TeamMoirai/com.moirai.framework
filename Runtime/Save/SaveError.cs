namespace Moirai.Atropos.Persistence
{
    public enum SaveError : byte
    {
        None = 0,
        UnknownSerializer = 1,
        MissingBlock = 2,
        InvalidFormat = 3,
        CommitOutcomeUnknown = 4
    }
}
