using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    public interface IProcedureCatalog
    {
        string EntranceKey { get; }
        IReadOnlyDictionary<string, Func<IProcedureSwitcher, IProcedure>> Factories { get; }
    }
}
