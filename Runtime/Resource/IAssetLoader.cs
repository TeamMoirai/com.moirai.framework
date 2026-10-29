using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Assets
{
    public interface IAssetLoader
    {
        UniTask<IAssetPayload<T>> LoadAssetAsync<T>(ResourceKey key, CancellationToken ownerToken)
            where T : UnityEngine.Object;
        UniTask<IScenePayload> LoadSceneAsync(ResourceKey key, LoadSceneParameters parameters,
            bool activateOnLoad, CancellationToken ownerToken);
    }
}
