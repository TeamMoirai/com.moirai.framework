using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源弱引用基类：Inspector 经 GUID 引用资源，运行时按需异步加载，序列化快照不携带资源本体。
    /// </summary>
    /// <remarks>
    /// 加载机制全部住在本类（租约自持、已加载幂等回放、在途并发并入、释放代偿），派生类只需声明 <see cref="AssetType"/>，无需重复实现； <br />
    /// GUID 到定位地址的解析走后端清单（YooAsset 收集器须勾选 IncludeAssetGUID），服务未就绪时（编辑器预览与工具面）回退 AssetDatabase； <br />
    /// 运行时态（租约与已加载对象）不序列化，域重载即复位。
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// /// <summary>
    /// /// GameObject 资源弱引用
    /// /// </summary>
    /// [Serializable]
    /// public class AssetReferenceGameObject : AssetReference
    /// {
    ///     public override Type AssetType => typeof(GameObject);
    /// }
    /// ]]>
    /// </code></example>
    [Serializable]
    public abstract class AssetReference
    {
        [Tooltip("资源 GUID")]
        // ReSharper disable once InconsistentNaming
        [SerializeField] internal string m_GUID;

        [Tooltip("资源包名称，留空使用默认资源包")]
        [SerializeField] internal string m_PackageName = "";

        [NonSerialized] private ResourceLeaseHandle _handle;
        [NonSerialized] private UObject _asset;
        [NonSerialized] private UniTask<UObject>? _pending;
        [NonSerialized] private bool _releasePending;

        /// <summary>资源 GUID。</summary>
        // ReSharper disable once InconsistentNaming
        public string GUID { get => m_GUID; internal set => m_GUID = value ?? string.Empty; }

        /// <summary>资源包名称，空串使用默认资源包。</summary>
        public string PackageName { get => m_PackageName; internal set => m_PackageName = value ?? string.Empty; }

        /// <summary>该引用负责加载的资源类型，由派生类指定。</summary>
        public abstract Type AssetType { get; }

        /// <summary>是否已加载。</summary>
        public bool IsLoaded => _asset != null;

#if UNITY_EDITOR
        /// <summary>编辑器预览资源：GUID 直读 AssetDatabase，不建运行时记录、不取租约。</summary>
        public UObject EditorAsset => string.IsNullOrEmpty(m_GUID)
            ? null
            : UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(m_GUID), AssetType);
#endif

        /// <summary>
        /// 解析当前清单里该 GUID 的资源定位地址。
        /// </summary>
        /// <param name="location">解析出的定位地址；失败为 <c>null</c>。</param>
        /// <returns>解析成功为 <c>true</c>。</returns>
        public bool TryGetLocation(out string location) =>
            ResourceService.TryGetLocationFromGuid(m_GUID, out location, m_PackageName);

        /// <summary>
        /// 检查运行时引用键是否有效（GUID 能解析到资源）。
        /// </summary>
        public bool RuntimeKeyIsValid() => !string.IsNullOrEmpty(m_GUID) && TryGetLocation(out _);

        /// <summary>
        /// 异步加载引用的资源并持有租约，直到 <see cref="ReleaseAsset"/>。
        /// </summary>
        /// <remarks>
        /// 主线程调用；已加载时幂等回放缓存对象，在途时并发调用并入同一次加载，失败或取消返回 <c>null</c> 且允许重试； <br />
        /// 服务未就绪时（编辑器预览与工具面）回退 AssetDatabase 直读，不建记录也不取租约。
        /// </remarks>
        /// <param name="cancellationToken">取消令牌；取消后返回 <c>null</c>。</param>
        /// <returns>加载的资源对象；GUID 为空、解析失败或加载失败为 <c>null</c>。</returns>
        public async UniTask<UObject> LoadAssetAsync(CancellationToken cancellationToken = default)
        {
            if (_asset != null)
            {
                return _asset;
            }

            // 在途并发并入同一次加载：Preserve 使同一任务可被多方多次等待
            if (_pending.HasValue)
            {
                return await _pending.Value;
            }

            _releasePending = false;
            UniTask<UObject> load = LoadAssetCoreAsync(cancellationToken).Preserve();
            _pending = load;

            UObject asset = await load;
            _pending = null;
            return asset;
        }

        /// <summary>
        /// 释放已加载的租约；未加载时为空操作。
        /// </summary>
        /// <remarks>加载在途时调用，核心路径会在加载完成后代为释放租约并丢弃结果。</remarks>
        public void ReleaseAsset()
        {
            if (_handle.IsValid)
            {
                // 服务关停后记录已随 Store 清空，无处释放也无需释放
                if (ResourceService.IsInitialized)
                {
                    ResourceService.Release(_handle);
                }
            }
            else
            {
                _releasePending = true;
            }

            _handle = ResourceLeaseHandle.Invalid;
            _asset = null;
            _pending = null;
        }

        /// <summary>
        /// 加载核心路径：解析 GUID、取直接租约、落地缓存。
        /// </summary>
        private async UniTask<UObject> LoadAssetCoreAsync(CancellationToken cancellationToken)
        {
            if (ResourceService.IsInitialized)
            {
                if (!TryGetLocation(out string location))
                {
                    LogUtility.Warning(
                        "AssetReference load failed: GUID not resolved from package manifest (check IncludeAssetGUID in the YooAsset collector settings). GUID:{0} Package:{1}",
                        GUID, PackageName);
                    return null;
                }

                // 与 LoadLeaseAsync<T> 同一管线：显式 ResourceKey 承载 AssetType（assetKind 由后端归一化），取直接租约后按句柄读对象
                ResourceLeaseHandle handle = await ResourceService.AcquireDirectAsync(
                    new ResourceKey(location, m_PackageName, AssetType), cancellationToken);
                if (!handle.IsValid)
                {
                    return null;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    ResourceService.Release(handle);
                    return null;
                }

                UObject asset = ResourceService.TryGetLeaseAsset(handle, out UObject loaded) ? loaded : null;
                if (asset == null)
                {
                    ResourceService.Release(handle);
                    return null;
                }

                if (_releasePending)
                {
                    ResourceService.Release(handle);
                    return null;
                }

                _handle = handle;
                _asset = asset;
                return _asset;
            }

#if UNITY_EDITOR
            _asset = string.IsNullOrEmpty(m_GUID)
                ? null
                : UnityEditor.AssetDatabase.LoadAssetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(m_GUID), AssetType);
            return _asset;
#else
            return null;
#endif
        }
    }
}
