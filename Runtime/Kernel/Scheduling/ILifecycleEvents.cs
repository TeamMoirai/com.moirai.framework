using System;

namespace Moirai.Atropos.Kernel.Scheduling
{
    public interface ILifecycleEvents
    {
        event Action<bool> ApplicationFocus;
        event Action ApplicationQuit;
    }
}
