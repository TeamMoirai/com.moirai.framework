using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Assets
{
    public interface IScenePayload
    {
        UnityEngine.SceneManagement.Scene Scene { get; }
        UniTask DisposeAsync();
    }
}
