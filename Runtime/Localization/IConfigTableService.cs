using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.L10n
{
    public interface IConfigTableService
    {
        bool TryGetTable<T>(out T table) where T : class;
        UniTask<T> GetTableAsync<T>(CancellationToken cancellationToken = default) where T : class;
    }
}
