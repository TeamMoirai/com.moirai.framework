using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Kernel.Composition
{
    public interface IAsyncDisposableService
    {
        UniTask DisposeAsync();
    }
}
