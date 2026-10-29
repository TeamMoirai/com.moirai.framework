using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Assets
{
    public interface IAssetPayload<out T> where T : UnityEngine.Object
    {
        T Asset { get; }
        UniTask DisposeAsync();
    }
}
