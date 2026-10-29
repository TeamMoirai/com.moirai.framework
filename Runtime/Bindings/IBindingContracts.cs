using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Bindings
{
    public interface IBindingTarget
    {
        bool IsAlive { get; }
        void Apply(object asset);
    }

    public interface IBindingAssetLease : IDisposable
    {
        object Asset { get; }
    }

    public interface IBindingAssetSource
    {
        UniTask<IBindingAssetLease> AcquireAsync(string addressKey, CancellationToken cancellationToken);
    }

    public interface IResourceBindingService
    {
        UniTask BindAsync(string slotId, string addressKey, IBindingTarget target,
            CancellationToken cancellationToken = default);
        void Unbind(string slotId);
    }
}
