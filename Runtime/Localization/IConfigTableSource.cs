using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.L10n
{
    public interface IConfigTableSource
    {
        UniTask<T> LoadAsync<T>(CancellationToken cancellationToken) where T : class;
    }
}
