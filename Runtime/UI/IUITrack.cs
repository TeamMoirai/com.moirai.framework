using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Screens
{
    public interface IUITrack
    {
        string TrackId { get; }
        int Layer { get; }
        UniTask<IUIWindow> CreateAsync(string windowId, CancellationToken cancellationToken);
        void Destroy(IUIWindow window);
    }
}
