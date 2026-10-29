using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Plugins
{
    public interface IPluginCatalog
    {
        IReadOnlyList<Func<IPlugin>> Factories { get; }
        PluginCapability GrantedCapabilities { get; }
    }
}
