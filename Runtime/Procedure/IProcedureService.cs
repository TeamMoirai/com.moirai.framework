using Moirai.Atropos.Kernel.Composition;

namespace Moirai.Atropos.Procedure
{
    public interface IProcedureService : IProcedureSwitcher, IAsyncInitializable, IAsyncStoppable
    {
        bool IsRunning { get; }
    }
}
