using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 八字节的假租约接缝：只记调用，不做任何真实记账。
    /// <para>能这么假的全部前提是把后端契约收成 <see cref="IResourceLeaseSource"/> 八个成员：收之前
    /// 假后端要落 74 个抽象成员，等于不可 mock。</para>
    /// </summary>
    internal sealed class StubLeaseSource : IResourceLeaseSource
    {
        private readonly List<ResourceLeaseHandle> _released = new List<ResourceLeaseHandle>();

        /// <summary>
        /// 置为 true 后异步子资源图集取用直接抛 <see cref="GameException"/>，
        /// 用来复现"预约位已落地、取用却失败"这条窗口。
        /// </summary>
        public bool SubAssetsAcquireThrows { get; set; }

        public IReadOnlyList<ResourceLeaseHandle> Released => _released;

        public int ReleaseCalls => _released.Count;

        public int SetOptionsCalls;

        public ResourceLeaseHandle AcquireBinding(ResourceKey key) => new ResourceLeaseHandle(1, 1);

        public UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key,
            CancellationToken cancellationToken) =>
            UniTask.FromResult(new ResourceLeaseHandle(1, 1));

        public UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName,
            EResourceLeaseOption options, CancellationToken cancellationToken)
        {
            if (SubAssetsAcquireThrows)
            {
                throw new GameException("StubLeaseSource.AcquireSubAssetsBindingAsync is not implemented.");
            }

            return UniTask.FromResult(new ResourceLeaseHandle(1, 1));
        }

        public bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            sprite = null;
            return false;
        }

        public bool TryGetLeaseAsset(ResourceLeaseHandle handle, out Object asset)
        {
            asset = null;
            return handle.IsValid;
        }

        public bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
        {
            assetId = 42;
            return true;
        }

        public void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options) =>
            SetOptionsCalls++;

        public void Release(ResourceLeaseHandle handle) => _released.Add(handle);
    }
}
