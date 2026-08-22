using System;
using System.Collections.Generic;
using YooAsset;

namespace Moirai.Atropos.Resource.Internal
{
    /// <summary>
    /// 资源引用计数缓存。替代原 ObjectPool&lt;AssetObject&gt; 的引用计数功能。
    /// </summary>
    internal sealed class AssetReferenceCache
    {
        #region 结构体 [STRUCTS]

        private struct CacheEntry
        {
            public object asset;
            public AssetHandle handle;
            public int refCount;
            public float lastUseTime;
        }

        #endregion

        #region 字段 [FIELDS]

        private readonly Dictionary<string, CacheEntry> _entries = new Dictionary<string, CacheEntry>(64);
        private readonly Dictionary<object, string> _assetToKey = new Dictionary<object, string>(64);
        private float _checkInterval = 60f;
        private int _capacity = int.MaxValue;
        private float _expireTime = 60f;
        private float _checkTime;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取或设置检查间隔（秒）。
        /// </summary>
        public float CheckInterval
        {
            get => _checkInterval;
            set => _checkInterval = value;
        }

        /// <summary>
        /// 获取或设置容量上限。
        /// </summary>
        public int Capacity
        {
            get => _capacity;
            set => _capacity = value;
        }

        /// <summary>
        /// 获取或设置过期秒数。
        /// </summary>
        public float ExpireTime
        {
            get => _expireTime;
            set => _expireTime = value;
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 初始化缓存。
        /// </summary>
        public void Initialize()
        {
        }

        /// <summary>
        /// 尝试获取已缓存的资源（引用计数 +1）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="asset">资源对象。</param>
        /// <returns>是否存在。</returns>
        public bool TrySpawn(string location, out object asset)
        {
            if (location != null && _entries.TryGetValue(location, out CacheEntry entry))
            {
                entry.refCount++;
                entry.lastUseTime = UnityEngine.Time.realtimeSinceStartup;
                _entries[location] = entry;
                asset = entry.asset;
                return true;
            }

            asset = null;
            return false;
        }

        /// <summary>
        /// 注册新资源（引用计数 = 1）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="asset">资源对象。</param>
        /// <param name="handle">YooAsset 句柄。</param>
        public void Register(string location, object asset, AssetHandle handle)
        {
            if (location == null || asset == null)
            {
                return;
            }

            _entries[location] = new CacheEntry
            {
                asset = asset,
                handle = handle,
                refCount = 1,
                lastUseTime = UnityEngine.Time.realtimeSinceStartup
            };
            _assetToKey[asset] = location;
        }

        /// <summary>
        /// 释放引用（引用计数 -1）。
        /// </summary>
        /// <param name="asset">资源对象。</param>
        public void Release(object asset)
        {
            if (asset == null || !_assetToKey.TryGetValue(asset, out string location))
            {
                return;
            }

            if (!_entries.TryGetValue(location, out CacheEntry entry))
            {
                return;
            }

            entry.refCount--;
            entry.lastUseTime = UnityEngine.Time.realtimeSinceStartup;
            _entries[location] = entry;
        }

        /// <summary>
        /// 释放所有未使用的资源。
        /// </summary>
        public void ReleaseAllUnused()
        {
            List<string> toRemove = null;
            foreach (var kv in _entries)
            {
                if (kv.Value.refCount <= 0)
                {
                    toRemove ??= new List<string>();
                    toRemove.Add(kv.Key);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            foreach (string key in toRemove)
            {
                DisposeEntry(_entries[key]);
                _assetToKey.Remove(_entries[key].asset);
                _entries.Remove(key);
            }
        }

        /// <summary>
        /// 关闭缓存，释放所有资源。
        /// </summary>
        public void Shutdown()
        {
            foreach (var kv in _entries)
            {
                DisposeEntry(kv.Value);
            }

            _entries.Clear();
            _assetToKey.Clear();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static void DisposeEntry(CacheEntry entry)
        {
            AssetHandle handle = entry.handle;
            if (handle != null && handle.IsValid)
            {
                handle.Dispose();
            }
        }

        #endregion
    }
}
