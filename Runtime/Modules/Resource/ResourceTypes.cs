using System;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源租约句柄，用于安全引用资源记录。
    /// </summary>
    public readonly struct ResourceLeaseHandle
    {
        /// <summary>
        /// 无效句柄。
        /// </summary>
        public static readonly ResourceLeaseHandle Invalid = new ResourceLeaseHandle(-1, 0);

        /// <summary>
        /// 槽位索引。
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// 代际标记，用于检测槽位是否已被复用。
        /// </summary>
        public readonly uint Generation;

        /// <summary>
        /// 创建资源租约句柄。
        /// </summary>
        /// <param name="index">槽位索引。</param>
        /// <param name="generation">代际标记。</param>
        public ResourceLeaseHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        /// <summary>
        /// 是否有效。
        /// </summary>
        public bool IsValid => Index >= 0 && Generation != 0;
    }

    /// <summary>
    /// 类型化资源租约，持有资源对象并在 Dispose 时自动释放引用。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    public struct ResourceAssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private ResourceHandler _handler;
        private ResourceLeaseHandle _handle;

        /// <summary>
        /// 创建类型化资源租约。
        /// </summary>
        /// <param name="handler">资源处理器。</param>
        /// <param name="handle">租约句柄。</param>
        /// <param name="asset">资源对象。</param>
        public ResourceAssetLease(ResourceHandler handler, ResourceLeaseHandle handle, T asset)
        {
            _handler = handler;
            _handle = handle;
            Asset = asset;
        }

        /// <summary>
        /// 资源对象。
        /// </summary>
        public T Asset { get; private set; }

        /// <summary>
        /// 租约句柄。
        /// </summary>
        public ResourceLeaseHandle Handle => _handle;

        /// <summary>
        /// 是否有效。
        /// </summary>
        public bool IsValid => _handle.IsValid && Asset != null;

        /// <summary>
        /// 释放租约。
        /// </summary>
        public void Dispose()
        {
            if (!_handle.IsValid)
            {
                return;
            }

            _handler?.Release(_handle);
            _handler = null;
            _handle = ResourceLeaseHandle.Invalid;
            Asset = null;
        }
    }

    /// <summary>
    /// 资源标识键，描述资源的位置、类型和种类。
    /// </summary>
    public readonly struct ResourceKey
    {
        /// <summary>
        /// 加载键 ID（内部使用）。
        /// </summary>
        public readonly int LoadKeyId;

        /// <summary>
        /// 视图键 ID（内部使用）。
        /// </summary>
        public readonly int ViewKeyId;

        /// <summary>
        /// 资源包名称。
        /// </summary>
        public readonly string PackageName;

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public readonly string Location;

        /// <summary>
        /// 资源类型。
        /// </summary>
        public readonly Type AssetType;

        /// <summary>
        /// 资源种类。
        /// </summary>
        public readonly EResourceAssetKind AssetKind;

        /// <summary>
        /// 创建资源标识键。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="assetType">资源类型。</param>
        /// <param name="assetKind">资源种类。</param>
        public ResourceKey(string location, string packageName = "", Type assetType = null,
            EResourceAssetKind assetKind = EResourceAssetKind.Unknown)
        {
            LoadKeyId = 0;
            ViewKeyId = 0;
            PackageName = packageName ?? string.Empty;
            Location = location ?? string.Empty;
            AssetType = assetType;
            AssetKind = assetKind;
        }

        /// <summary>
        /// 创建资源标识键（内部使用）。
        /// </summary>
        /// <param name="loadKeyId">加载键 ID。</param>
        /// <param name="viewKeyId">视图键 ID。</param>
        public ResourceKey(int loadKeyId, int viewKeyId)
        {
            LoadKeyId = loadKeyId;
            ViewKeyId = viewKeyId;
            PackageName = string.Empty;
            Location = string.Empty;
            AssetType = null;
            AssetKind = EResourceAssetKind.Unknown;
        }

        /// <summary>
        /// 是否已解析 ID。
        /// </summary>
        public bool HasResolvedIds => LoadKeyId > 0;

        /// <summary>
        /// 创建指定类型的资源标识键。
        /// </summary>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源标识键。</returns>
        public static ResourceKey Asset<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return new ResourceKey(location, packageName, typeof(T), EResourceAssetKind.Asset);
        }
    }

    /// <summary>
    /// 资源种类。
    /// </summary>
    public enum EResourceAssetKind : byte
    {
        /// <summary>
        /// 未知。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 通用资源。
        /// </summary>
        Asset = 1,

        /// <summary>
        /// 精灵。
        /// </summary>
        Sprite = 2,

        /// <summary>
        /// 材质。
        /// </summary>
        Material = 3,

        /// <summary>
        /// 预制体。
        /// </summary>
        Prefab = 4,

        /// <summary>
        /// 子资源集。
        /// </summary>
        SubAssets = 5,
    }

    /// <summary>
    /// 资源状态。
    /// </summary>
    public enum EResourceAssetState : byte
    {
        /// <summary>
        /// 已释放。
        /// </summary>
        Released = 0,

        /// <summary>
        /// 加载中。
        /// </summary>
        Loading = 1,

        /// <summary>
        /// 活跃中。
        /// </summary>
        Active = 2,

        /// <summary>
        /// 保持存活。
        /// </summary>
        KeepAlive = 3,

        /// <summary>
        /// 空闲。
        /// </summary>
        Idle = 4,
    }

    /// <summary>
    /// 资源句柄种类（内部使用）。
    /// </summary>
    internal enum EResourceHandleKind : byte
    {
        /// <summary>
        /// 无。
        /// </summary>
        None = 0,

        /// <summary>
        /// 单资源句柄。
        /// </summary>
        AssetHandle = 1,

        /// <summary>
        /// 子资源句柄。
        /// </summary>
        SubAssetsHandle = 2,
    }

    /// <summary>
    /// 租约种类（内部使用）。
    /// </summary>
    internal enum EResourceLeaseKind : byte
    {
        /// <summary>
        /// 无。
        /// </summary>
        None = 0,

        /// <summary>
        /// 直接引用。
        /// </summary>
        Direct = 1,

        /// <summary>
        /// 绑定引用。
        /// </summary>
        Binding = 2,
    }

    /// <summary>
    /// 租约状态（内部使用）。
    /// </summary>
    internal enum EResourceLeaseState : byte
    {
        /// <summary>
        /// 空闲。
        /// </summary>
        Free = 0,

        /// <summary>
        /// 活跃。
        /// </summary>
        Active = 1,

        /// <summary>
        /// 已释放。
        /// </summary>
        Released = 2,
    }

    /// <summary>
    /// 租约选项（内部使用）。
    /// </summary>
    [Flags]
    internal enum EResourceLeaseOption : byte
    {
        /// <summary>
        /// 无。
        /// </summary>
        None = 0,

        /// <summary>
        /// 释放时保持存活。
        /// </summary>
        KeepAliveOnRelease = 1,
    }

    /// <summary>
    /// 资源信息快照，用于诊断查询。
    /// </summary>
    public struct ResourceAssetInfo
    {
        /// <summary>
        /// 加载键 ID。
        /// </summary>
        public int LoadKeyId;

        /// <summary>
        /// 资源包名称。
        /// </summary>
        public string Package;

        /// <summary>
        /// 资源定位地址。
        /// </summary>
        public string Location;

        /// <summary>
        /// 类型名称。
        /// </summary>
        public string TypeName;

        /// <summary>
        /// 资源种类。
        /// </summary>
        public EResourceAssetKind Kind;

        /// <summary>
        /// 资源状态。
        /// </summary>
        public EResourceAssetState State;

        /// <summary>
        /// 直接引用计数。
        /// </summary>
        public int DirectRefCount;

        /// <summary>
        /// 遗留直接引用计数。
        /// </summary>
        public int LegacyDirectRefCount;

        /// <summary>
        /// 绑定引用计数。
        /// </summary>
        public int BindingRefCount;

        /// <summary>
        /// 保持存活引用计数。
        /// </summary>
        public int KeepAliveRefCount;

        /// <summary>
        /// 保持存活剩余秒数。
        /// </summary>
        public float KeepAliveExpireIn;

        /// <summary>
        /// 空闲剩余秒数。
        /// </summary>
        public float IdleExpireIn;

        /// <summary>
        /// 总引用计数。
        /// </summary>
        public int RefCountTotal;

        /// <summary>
        /// 是否已请求空闲释放。
        /// </summary>
        public bool IdleReleaseRequested;

        /// <summary>
        /// 句柄是否有效。
        /// </summary>
        public bool HandleValid;

        /// <summary>
        /// 句柄种类。
        /// </summary>
        public byte HandleKind;
    }

    /// <summary>
    /// 绑定信息快照，用于诊断查询。
    /// </summary>
    public struct ResourceBindingInfo
    {
        /// <summary>
        /// 是否活跃。
        /// </summary>
        public bool Active;

        /// <summary>
        /// 绑定索引。
        /// </summary>
        public int BindingIndex;

        /// <summary>
        /// 所有者 ID。
        /// </summary>
        public int OwnerId;

        /// <summary>
        /// 所有者代际。
        /// </summary>
        public uint OwnerGeneration;

        /// <summary>
        /// 目标 GameObject ID。
        /// </summary>
        public ulong TargetGameObjectId;

        /// <summary>
        /// 目标组件 ID。
        /// </summary>
        public ulong TargetComponentId;

        /// <summary>
        /// 槽位键。
        /// </summary>
        public ulong SlotKey;

        /// <summary>
        /// 资源 ID。
        /// </summary>
        public int AssetId;

        /// <summary>
        /// 视图键 ID。
        /// </summary>
        public int ViewKeyId;

        /// <summary>
        /// 租约句柄。
        /// </summary>
        public ResourceLeaseHandle Lease;

        /// <summary>
        /// 版本号。
        /// </summary>
        public uint Version;

        /// <summary>
        /// 子索引。
        /// </summary>
        public ushort SubIndex;

        /// <summary>
        /// 槽位类型。
        /// </summary>
        public EResourceBindingSlotType SlotType;

        /// <summary>
        /// 是否有已应用资源。
        /// </summary>
        public bool HasAppliedAsset;

        /// <summary>
        /// 是否有运行时对象。
        /// </summary>
        public bool HasRuntimeObject;

#if UNITY_EDITOR
        /// <summary>
        /// 目标对象（仅编辑器）。
        /// </summary>
        public UnityEngine.Object TargetObject;
#endif
    }

    /// <summary>
    /// 所有者信息快照，用于诊断查询。
    /// </summary>
    public struct ResourceOwnerInfo
    {
        /// <summary>
        /// 是否活跃。
        /// </summary>
        public bool Active;

        /// <summary>
        /// 所有者索引。
        /// </summary>
        public int OwnerIndex;

        /// <summary>
        /// 所有者 ID。
        /// </summary>
        public int OwnerId;

        /// <summary>
        /// GameObject ID。
        /// </summary>
        public ulong GameObjectId;

        /// <summary>
        /// 代际标记。
        /// </summary>
        public uint Generation;

        /// <summary>
        /// 绑定数量。
        /// </summary>
        public int BindingCount;

        /// <summary>
        /// 已注册目标数量。
        /// </summary>
        public int RegisteredTargetCount;

        /// <summary>
        /// 是否有所有者对象。
        /// </summary>
        public bool HasOwnerObject;

#if UNITY_EDITOR
        /// <summary>
        /// 所有者对象（仅编辑器）。
        /// </summary>
        public UnityEngine.GameObject OwnerObject;
#endif
    }
}
