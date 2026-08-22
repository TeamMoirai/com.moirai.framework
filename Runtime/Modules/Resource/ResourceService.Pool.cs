namespace Moirai.Atropos.Resource
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal partial class ResourceService
    {
        /// <summary>
        /// 资源自动释放检查间隔（秒）。
        /// </summary>
        public float AssetAutoReleaseInterval
        {
            get => _assetCache.CheckInterval;
            set => _assetCache.CheckInterval = value;
        }

        /// <summary>
        /// 资源容量上限。
        /// </summary>
        public int AssetCapacity
        {
            get => _assetCache.Capacity;
            set => _assetCache.Capacity = value;
        }

        /// <summary>
        /// 资源过期秒数。
        /// </summary>
        public float AssetExpireTime
        {
            get => _assetCache.ExpireTime;
            set => _assetCache.ExpireTime = value;
        }

        /// <summary>
        /// 资源池优先级。
        /// </summary>
        public int AssetPriority { get; set; } = 0;

        /// <summary>
        /// 卸载资源（引用计数 -1）。
        /// </summary>
        /// <param name="asset">要卸载的资源。</param>
        public void UnloadAsset(object asset)
        {
            _assetCache.Release(asset);
        }
    }
}
