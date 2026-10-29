using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Kernel.Composition
{
    public interface IAsyncStoppable
    {
        UniTask StopAsync(CancellationToken cancellationToken);
    }
}
