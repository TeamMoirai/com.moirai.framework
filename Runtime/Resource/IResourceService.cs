using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Assets
{
    public interface IResourceService
    {
        bool TryGetAsset<T>(ResourceKey key, out T asset) where T : UnityEngine.Object;
        UniTask<ResourceLease<T>> AcquireLeaseAsync<T>(ResourceKey key,
            ResourceLeaseOption option = ResourceLeaseOption.None,
            CancellationToken cancellationToken = default) where T : UnityEngine.Object;
        UniTask<ISceneLease> LoadSceneAsync(ResourceKey key, LoadSceneParameters parameters,
            bool activateOnLoad, CancellationToken cancellationToken = default);
    }
}
