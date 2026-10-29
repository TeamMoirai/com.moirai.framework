using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Rendering
{
    public interface IGraphicsApplier
    {
        UniTask<GraphicsState> GetCurrentAsync(CancellationToken cancellationToken);
        UniTask ApplyResolutionAsync(ResolutionInfo resolution, bool fullScreen, WindowMode mode,
            CancellationToken cancellationToken);
        bool VSync { get; set; }
    }
}
