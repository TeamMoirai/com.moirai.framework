using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Plugins
{
    public interface IPluginServices
    {
        ILogger Logger { get; }
        IClock Clock { get; }
    }
}
