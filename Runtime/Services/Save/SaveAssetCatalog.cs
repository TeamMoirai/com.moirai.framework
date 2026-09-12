using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档资产引用目录：登记可被存档字段引用的资产（Texture/SO/Material 等）及其 ResourceService 定位串。
    /// <para>无代码保存的资产引用字段按「存定位串、读目录反查」持久化——捕获查 object → location，
    /// 恢复查 location → object（同一份目录双向解析，不触发运行时加载，保持捕获器同步契约与零租约负担）。
    /// 被引用资产须先登记入册，否则捕获写 Null 并记告警。</para>
    /// <para>查找表延迟构建（首次查询时），编辑器期经 <see cref="OnValidate"/> 失效重建；主线程契约。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "SaveAssetCatalog", menuName = "Moirai/Save Asset Catalog", order = 0)]
    public sealed class SaveAssetCatalog : ScriptableObject
    {
        /// <summary>
        /// 目录条目：资产引用 + ResourceService 定位串。
        /// </summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>资产引用。</summary>
            [SerializeField] internal Object m_Asset;

            /// <summary>ResourceService 定位串（YooAsset 地址）。</summary>
            [SerializeField] internal string m_Location = string.Empty;

            /// <summary>
            /// 资产引用。
            /// </summary>
            public Object Asset => m_Asset;

            /// <summary>
            /// ResourceService 定位串。
            /// </summary>
            public string Location => m_Location;
        }

        /// <summary>登记条目列表（Inspector 编辑）。</summary>
        [SerializeField] internal List<Entry> m_Entries = new List<Entry>();

        /// <summary>资产 → 定位串表（延迟构建）。</summary>
        private Dictionary<Object, string> _assetToLocation;

        /// <summary>定位串 → 资产表（延迟构建，Ordinal 比较——定位串为地址标识而非显示文本）。</summary>
        private Dictionary<string, Object> _locationToAsset;

        /// <summary>
        /// 登记条目数。
        /// </summary>
        public int Count => m_Entries.Count;

        /// <summary>
        /// 反查资产的定位串。
        /// </summary>
        /// <param name="asset">资产引用。</param>
        /// <param name="location">命中时的定位串。</param>
        /// <returns>已登记返回 <c>true</c>。</returns>
        public bool TryGetLocation(Object asset, out string location)
        {
            if (asset == null)
            {
                location = null;
                return false;
            }

            EnsureLookup();
            return _assetToLocation.TryGetValue(asset, out location);
        }

        /// <summary>
        /// 按定位串解析资产（类型不符按未命中处理——目录登记类型演进后与存档字段类型漂移的防御）。
        /// </summary>
        /// <typeparam name="T">期望资产类型。</typeparam>
        /// <param name="location">定位串（空/空白直接未命中）。</param>
        /// <param name="asset">命中时的资产。</param>
        /// <returns>命中且类型匹配返回 <c>true</c>。</returns>
        public bool TryResolve<T>(string location, out T asset) where T : Object
        {
            if (!string.IsNullOrEmpty(location))
            {
                EnsureLookup();
                if (_locationToAsset.TryGetValue(location, out Object resolved) && resolved != null && resolved is T typed)
                {
                    asset = typed;
                    return true;
                }
            }

            asset = null;
            return false;
        }

        /// <summary>
        /// 构建/重建双向查找表（重复资产或重复定位串首到先得并记告警——目录数据错误不应静默换绑）。
        /// </summary>
        private void EnsureLookup()
        {
            if (_assetToLocation != null)
            {
                return;
            }

            _assetToLocation = new Dictionary<Object, string>(m_Entries.Count);
            _locationToAsset = new Dictionary<string, Object>(m_Entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry entry = m_Entries[i];
                if (entry == null || entry.m_Asset == null || string.IsNullOrEmpty(entry.m_Location))
                {
                    continue;
                }

                if (_assetToLocation.ContainsKey(entry.m_Asset))
                {
                    LogUtility.Warning("[SaveService] SaveAssetCatalog '{0}' has duplicate asset entry '{1}' — first entry wins.", name, entry.m_Asset.name);
                    continue;
                }

                if (_locationToAsset.ContainsKey(entry.m_Location))
                {
                    LogUtility.Warning("[SaveService] SaveAssetCatalog '{0}' has duplicate location '{1}' — first entry wins.", name, entry.m_Location);
                    continue;
                }

                _assetToLocation.Add(entry.m_Asset, entry.m_Location);
                _locationToAsset.Add(entry.m_Location, entry.m_Asset);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器期条目变更后失效查找表（下次查询重建）。
        /// </summary>
        private void OnValidate()
        {
            _assetToLocation = null;
            _locationToAsset = null;
        }
#endif
    }
}
