using System;

namespace Moirai.Atropos.Plugins
{
    [Flags]
    public enum PluginCapability : uint
    {
        None = 0,
        Resource = 1,
        Audio = 2,
        Input = 4,
        Save = 8,
        Network = 16,
        Telemetry = 32
    }
}
