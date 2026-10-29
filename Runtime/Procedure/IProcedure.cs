using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Procedure
{
    public interface IProcedure
    {
        string Key { get; }
        UniTask OnEnterAsync(CancellationToken cancellationToken);
        void OnUpdate(float elapsedSeconds, float realElapsedSeconds);
        UniTask OnLeaveAsync(bool isShutdown, CancellationToken cancellationToken);
    }
}
