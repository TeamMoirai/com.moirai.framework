using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Sound
{
    public interface IAudioClipLeaseSource
    {
        UniTask<IAudioClipLease> AcquireAsync(string clipKey, CancellationToken cancellationToken);
    }
}
