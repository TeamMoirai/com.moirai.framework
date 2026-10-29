using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scenes
{
    public interface ISceneLease
    {
        UnityEngine.SceneManagement.Scene Scene { get; }
        UniTask UnloadAsync(System.Threading.CancellationToken cancellationToken);
    }
}
