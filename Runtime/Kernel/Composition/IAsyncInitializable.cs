using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Kernel.Composition
{
    public interface IAsyncInitializable
    {
        UniTask InitializeAsync(CancellationToken cancellationToken);
    }
}
