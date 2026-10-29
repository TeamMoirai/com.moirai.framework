namespace Moirai.Atropos.Persistence
{
    public readonly struct SaveCommit
    {
        public SaveCommit(int slot, long sequence, bool powerLossDurable)
        {
            Slot = slot;
            Sequence = sequence;
            PowerLossDurable = powerLossDurable;
        }

        public int Slot { get; }
        public long Sequence { get; }
        public bool PowerLossDurable { get; }
    }
}
