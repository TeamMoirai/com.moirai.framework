using System.Threading;

namespace Moirai.Atropos.Plugins
{
    public interface IPluginContext
    {
        PluginId Id { get; }
        PluginCapability GrantedCapabilities { get; }
        IPluginServices Services { get; }
        CancellationToken LifetimeToken { get; }
    }
}
