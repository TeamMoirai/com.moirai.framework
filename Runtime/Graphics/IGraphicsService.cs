using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Rendering
{
    public interface IGraphicsService
    {
        event Action<ResolutionInfo> ResolutionChanged;
        event Action<bool> FullScreenChanged;
        event Action<WindowMode> WindowModeChanged;
        event Action<bool> VSyncChanged;
        GraphicsState State { get; }
        UniTask SetResolutionAsync(ResolutionInfo resolution, CancellationToken cancellationToken);
        UniTask SetFullScreenAsync(bool fullScreen, CancellationToken cancellationToken);
        UniTask SetWindowModeAsync(WindowMode mode, CancellationToken cancellationToken);
        void SetVSync(bool enabled);
        void RemoveAllSettings();
    }
}
