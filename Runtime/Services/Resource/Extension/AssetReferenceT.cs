using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 类型化资源弱引用：经 GUID 引用 <typeparamref name="TObject"/> 资源，运行时按需异步加载。
    /// </summary>
    /// <typeparam name="TObject">资源类型。</typeparam>
    /// <remarks>
    /// 加载机制全部继承自 <see cref="AssetReference"/>（已加载幂等回放、在途并发并入、租约自持、取消回 <c>null</c> 可重试）， <br />
    /// 本类只把资源类型钉成 <typeparamref name="TObject"/> 并提供类型化投影，派生自本类的更细类型同样无需实现任何加载逻辑； <br />
    /// 只需短暂取值、无需自持生命周期的调用方改走 <see cref="ResourceService.TryLoadAssetAsync{T}"/> 的保活窗口口径。
    /// </remarks>
    [Serializable]
    public class AssetReference<TObject> : AssetReference where TObject : UObject
    {
        /// <summary>该引用负责加载的资源类型。</summary>
        public sealed override Type AssetType => typeof(TObject);

        /// <summary>
        /// 异步加载引用的资源并持有租约，直到 <see cref="AssetReference.ReleaseAsset"/>。
        /// </summary>
        /// <param name="cancellationToken">取消令牌；取消后返回 <c>null</c>。</param>
        /// <returns>加载的资源对象；GUID 为空、解析失败或加载失败为 <c>null</c>。</returns>
        public new async UniTask<TObject> LoadAssetAsync(CancellationToken cancellationToken = default) =>
            (TObject)await base.LoadAssetAsync(cancellationToken);

#if UNITY_EDITOR
        /// <summary>编辑器预览资源：GUID 直读 AssetDatabase，不建运行时记录、不取租约。</summary>
        public new TObject EditorAsset => (TObject)base.EditorAsset;
#endif
    }
}
