using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    public sealed class ProcedureEnteredEvent
    {
        public ProcedureEnteredEvent(string key) { Key = key; }
        public string Key { get; }
    }

    public sealed class ProcedureLeftEvent
    {
        public ProcedureLeftEvent(string key, bool isShutdown) { Key = key; IsShutdown = isShutdown; }
        public string Key { get; }
        public bool IsShutdown { get; }
    }

    public sealed class ProcedureCrashedEvent
    {
        public ProcedureCrashedEvent(string key, Exception exception) { Key = key; Exception = exception; }
        public string Key { get; }
        public Exception Exception { get; }
    }

    public sealed class ProcedureCatalog : IProcedureCatalog
    {
        public ProcedureCatalog(string entranceKey,
            IReadOnlyDictionary<string, Func<IProcedureSwitcher, IProcedure>> factories)
        {
            EntranceKey = entranceKey ?? throw new ArgumentNullException(nameof(entranceKey));
            Factories = factories ?? throw new ArgumentNullException(nameof(factories));
        }

        public string EntranceKey { get; }
        public IReadOnlyDictionary<string, Func<IProcedureSwitcher, IProcedure>> Factories { get; }
    }
}
