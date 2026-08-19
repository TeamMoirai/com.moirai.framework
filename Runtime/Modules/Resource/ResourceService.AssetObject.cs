using Moirai.Atropos.ObjectPool;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal partial class ResourceService
    {
        /// <summary>
        /// 资源对象。
        /// </summary>
        private sealed class AssetObject : ObjectBase
        {
            private AssetHandle _assetHandle;
            private ResourceService _resourceService;


            public AssetObject()
            {
                _assetHandle = null;
            }

            public static AssetObject Create(string name, object target, object assetHandle, ResourceService resourceService)
            {
                if (assetHandle == null)
                {
                    throw new GameException("Resource is invalid.");
                }

                if (resourceService == null)
                {
                    throw new GameException("Resource Manager is invalid.");
                }

                AssetObject assetObject = MemoryPool.Acquire<AssetObject>();
                assetObject.Initialize(name, target);
                assetObject._assetHandle = (AssetHandle)assetHandle;
                assetObject._resourceService = resourceService;
                return assetObject;
            }

            public override void Clear()
            {
                base.Clear();
                _assetHandle = null;
            }

            protected internal override void OnDespawn()
            {
                base.OnDespawn();
            }

            protected internal override void Release(bool isShutdown)
            {
                if (!isShutdown)
                {
                    AssetHandle handle = _assetHandle;
                    if (handle is { IsValid: true })
                    {
                        handle.Dispose();
                    }
                    _assetHandle = null;
                }
            }
        }
    }
}