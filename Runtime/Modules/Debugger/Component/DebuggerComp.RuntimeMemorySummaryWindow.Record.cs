namespace Moirai.Atropos.Debugger
{
    public sealed partial class DebuggerComp
    {
        private sealed partial class RuntimeMemorySummaryWindow : ScrollableDebuggerWindowBase
        {
            private sealed class Record
            {
                private readonly string _name;
                private int _count;
                private long _size;

                public Record(string name)
                {
                    _name = name;
                    _count = 0;
                    _size = 0L;
                }

                public string Name => _name;

                public int Count
                {
                    get => _count;
                    set => _count = value;
                }

                public long Size
                {
                    get => _size;
                    set => _size = value;
                }
            }
        }
    }
}
