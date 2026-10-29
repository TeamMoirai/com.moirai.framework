using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Screens
{
    public interface IUIService
    {
        UniTask<UIOpenResult> OpenAsync(string windowId, CancellationToken cancellationToken);
        UniTask CloseAsync(string windowId, CancellationToken cancellationToken);
        IReadOnlyList<string> Stack(string trackId);
        IReadOnlyList<string> OpenWindows { get; }
    }
}
