using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Assets
{
    public interface IScenePayload
    {
        UnityEngine.SceneManagement.Scene Scene { get; }
        bool IsActivated { get; }
        UniTask ActivateAsync(System.Threading.CancellationToken cancellationToken);
        UniTask DisposeAsync();
    }
}
