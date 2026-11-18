using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 租约取用与归还的窄接缝：绑定层所需的全部后端能力，恰好九个成员。
    /// </summary>
    /// <remarks>
    /// 九个成员是 <see cref="IResourceService"/> 的公共契约成员，实现 <see cref="IResourceService"/> 即直接满足本接口；程序集外的后端实现契约即可落地。
    /// </remarks>
    internal interface IResourceLeaseSource
    {
        /// <summary>
        /// 同步取用一个直接租约；失败返回 <see cref="ResourceLeaseHandle.Invalid"/>。
        /// </summary>
        ResourceLeaseHandle AcquireBinding(ResourceKey key);

        /// <summary>
        /// 只读缓存取用：已加载则租约，未命中返回 false 且不发起加载。
        /// </summary>
        bool TryAcquireBindingCached(ResourceKey key, out ResourceLeaseHandle handle);

        /// <summary>
        /// 异步取用一个直接租约。
        /// </summary>
        UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key, CancellationToken cancellationToken);

        /// <summary>
        /// 异步取用整张子资源图集的租约，具体精灵再按名索取。
        /// </summary>
        UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName,
            EResourceLeaseOption options, CancellationToken cancellationToken);

        /// <summary>
        /// 从子资源图集租约里按名取出一个精灵。
        /// </summary>
        bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite);

        /// <summary>
        /// 取出租约指向的资源对象。
        /// </summary>
        bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset);

        /// <summary>
        /// 取出租约指向的记录 id，仅用于诊断。
        /// </summary>
        bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId);

        /// <summary>
        /// 登记取用时的租约选项（如释放后转保活）。
        /// </summary>
        void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options);

        /// <summary>
        /// 归还租约。<b>必须无条件完成</b>——静默丢掉一条就是永久泄漏。
        /// </summary>
        void Release(ResourceLeaseHandle handle);
    }
}
