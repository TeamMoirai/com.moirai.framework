using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Procedure
{
    public interface IProcedureSwitcher
    {
        string CurrentKey { get; }
        UniTask SwitchToAsync(string procedureKey, CancellationToken cancellationToken);
    }
}
