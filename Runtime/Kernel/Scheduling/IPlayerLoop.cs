using System;

namespace Moirai.Atropos.Kernel.Scheduling
{
    public interface IPlayerLoop
    {
        IDisposable AddUpdateListener(Action listener);
        IDisposable AddFixedUpdateListener(Action listener);
        IDisposable AddLateUpdateListener(Action listener);
    }
}
