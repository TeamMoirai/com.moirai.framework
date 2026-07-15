using Moirai.Atropos.ObjectPool;
using YooAsset;

namespace Moirai.Atropos.Resource
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal partial class ResourceModule
    {
        /// <summary>
        /// 资源对象。
        /// </summary>
        private sealed class AssetObject : ObjectBase
        {
            private AssetHandle m_AssetHandle;
            private ResourceModule _resourceModule;


            public AssetObject()
            {
                m_AssetHandle = null;
            }

            public static AssetObject Create(string name, object target, object assetHandle, ResourceModule resourceModule)
            {
                if (assetHandle == null)
                {
                    throw new GameException("Resource is invalid.");
                }

                if (resourceModule == null)
                {
                    throw new GameException("Resource Manager is invalid.");
                }

                AssetObject assetObject = MemoryPool.Acquire<AssetObject>();
                assetObject.Initialize(name, target);
                assetObject.m_AssetHandle = (AssetHandle)assetHandle;
                assetObject._resourceModule = resourceModule;
                return assetObject;
            }

            public override void Clear()
            {
                base.Clear();
                m_AssetHandle = null;
            }

            protected internal override void OnDespawn()
            {
                base.OnDespawn();
            }

            protected internal override void Release(bool isShutdown)
            {
                if (!isShutdown)
                {
                    AssetHandle handle = m_AssetHandle;
                    if (handle is { IsValid: true })
                    {
                        handle.Dispose();
                    }
                    m_AssetHandle = null;
                }
            }
        }
    }
}