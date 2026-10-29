using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;

namespace Moirai.Atropos.Pooling
{
    public interface IPrefabLease : IDisposable
    {
        UnityEngine.GameObject Prefab { get; }
    }

    public interface IPrefabSource
    {
        UniTask<IPrefabLease> AcquireAsync(ResourceKey key, CancellationToken cancellationToken);
    }
}
