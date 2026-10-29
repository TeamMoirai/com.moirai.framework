using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;

namespace Moirai.Atropos.Scenes
{
    public interface ISceneService
    {
        UniTask<ISceneLease> LoadAsync(ResourceKey key,
            UnityEngine.SceneManagement.LoadSceneParameters parameters, bool suspendActivation,
            CancellationToken cancellationToken = default);
        UniTask UnloadAsync(ISceneLease lease, CancellationToken cancellationToken = default);
    }
}
