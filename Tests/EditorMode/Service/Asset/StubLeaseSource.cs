using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Asset;
using UnityEngine;

namespace Service.Asset
{
    /// <summary>
    /// 八字节的假租约接缝：只记调用，不做任何真实记账。
    /// </summary>
    /// <remarks>
    /// 实现 <see cref="IAssetLeaseSource"/> 九个成员即可替换真实后端，无需 YooAssets 初始化。
    /// </remarks>
    internal sealed class StubLeaseSource : IAssetLeaseSource
    {
        private readonly List<AssetLeaseHandle> _released = new List<AssetLeaseHandle>();

        /// <summary>置为 <c>true</c> 后异步子资源图集取用直接抛 <see cref="MoiraiException"/>，用来造"预约位已落地、取用却失败"这条窗口。</summary>
        public bool SubAssetsAcquireThrows { get; set; }

        public IReadOnlyList<AssetLeaseHandle> Released => _released;

        public int ReleaseCalls => _released.Count;

        public int SetOptionsCalls;

        public AssetLeaseHandle AcquireBinding(AssetKey key) => new AssetLeaseHandle(1, 1);

        public bool TryAcquireBindingCached(AssetKey key, out AssetLeaseHandle handle)
        {
            handle = AssetLeaseHandle.Invalid;
            return false;
        }

        public UniTask<AssetLeaseHandle> AcquireBindingAsync(AssetKey key,
            CancellationToken cancellationToken) =>
            UniTask.FromResult(new AssetLeaseHandle(1, 1));

        public UniTask<AssetLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName,
            EAssetLeaseOption options, CancellationToken cancellationToken)
        {
            if (SubAssetsAcquireThrows)
            {
                throw new MoiraiException("StubLeaseSource.AcquireSubAssetsBindingAsync is not implemented.");
            }

            return UniTask.FromResult(new AssetLeaseHandle(1, 1));
        }

        public bool TryGetSubSpriteAsset(AssetLeaseHandle handle, string spriteName, out Sprite sprite)
        {
            sprite = null;
            return false;
        }

        public bool TryGetLeaseAsset(AssetLeaseHandle handle, out Object asset)
        {
            asset = null;
            return handle.IsValid;
        }

        public bool TryGetLeaseAssetId(AssetLeaseHandle handle, out int assetId)
        {
            assetId = 42;
            return true;
        }

        public void SetLeaseOptions(AssetLeaseHandle handle, EAssetLeaseOption options) =>
            SetOptionsCalls++;

        public void Release(AssetLeaseHandle handle) => _released.Add(handle);
    }
}
