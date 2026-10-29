using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Screens
{
    public interface IUIWindow
    {
        string WindowId { get; }
        UniTask OnShowAsync(CancellationToken cancellationToken);
        UniTask OnHideAsync(bool isShutdown, CancellationToken cancellationToken);
    }
}
