using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档预制体注册表：登记可持久化动态实体的预制体及其稳定键与 ResourceService 定位串。
    /// <para>动态实体按「存注册键、读注册表反查定位串」持久化——生成记录只存稳定键，恢复时经
    /// <see cref="ResourceService"/> 按定位串加载实例化。被持久化的预制体须先登记入册，
    /// 未登记键在生成/恢复期记录错误日志并跳过。</para>
    /// <para>查找表延迟构建（首次查询时），编辑器期经 <see cref="OnValidate"/> 失效重建；主线程契约。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "SavePrefabRegistry", menuName = "Moirai/Save Prefab Registry", order = 1)]
    public sealed class SavePrefabRegistry : ScriptableObject
    {
        /// <summary>
        /// 注册条目：稳定键 + 预制体引用 + ResourceService 定位串。
        /// </summary>
        [Serializable]
        public sealed class Entry
        {
            /// <summary>稳定键（持久化标识，存档内按此键引用；改名即毁档，须保持跨版本稳定）。</summary>
            [SerializeField] internal string m_Key = string.Empty;

            /// <summary>预制体引用（模板差分基准捕获与编辑器校验用）。</summary>
            [SerializeField] internal GameObject m_Prefab;

            /// <summary>ResourceService 定位串（YooAsset 地址；运行期生成/恢复的加载入口）。</summary>
            [SerializeField] internal string m_Location = string.Empty;

            /// <summary>
            /// 稳定键。
            /// </summary>
            public string Key => m_Key;

            /// <summary>
            /// 预制体引用。
            /// </summary>
            public GameObject Prefab => m_Prefab;

            /// <summary>
            /// ResourceService 定位串。
            /// </summary>
            public string Location => m_Location;
        }

        /// <summary>登记条目列表（Inspector 编辑）。</summary>
        [SerializeField] internal List<Entry> m_Entries = new List<Entry>();

        /// <summary>稳定键 → 条目表（延迟构建，Ordinal 比较——键为持久化标识而非显示文本）。</summary>
        private Dictionary<string, Entry> _keyToEntry;

        /// <summary>
        /// 登记条目数。
        /// </summary>
        public int Count => m_Entries.Count;

        /// <summary>
        /// 按稳定键反查注册条目。
        /// </summary>
        /// <param name="key">稳定键（空/空白直接未命中）。</param>
        /// <param name="entry">命中时的注册条目。</param>
        /// <returns>命中返回 <c>true</c>。</returns>
        public bool TryGetEntry(string key, out Entry entry)
        {
            if (!string.IsNullOrEmpty(key))
            {
                EnsureLookup();
                if (_keyToEntry.TryGetValue(key, out entry))
                {
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>
        /// 按稳定键反查 ResourceService 定位串。
        /// </summary>
        /// <param name="key">稳定键。</param>
        /// <param name="location">命中时的定位串。</param>
        /// <returns>命中且定位串非空返回 <c>true</c>。</returns>
        public bool TryGetLocation(string key, out string location)
        {
            if (TryGetEntry(key, out Entry entry) && !string.IsNullOrEmpty(entry.m_Location))
            {
                location = entry.m_Location;
                return true;
            }

            location = null;
            return false;
        }

        /// <summary>
        /// 按稳定键反查预制体引用（模板差分基准捕获用）。
        /// </summary>
        /// <param name="key">稳定键。</param>
        /// <param name="prefab">命中时的预制体。</param>
        /// <returns>命中且引用有效返回 <c>true</c>。</returns>
        public bool TryGetPrefab(string key, out GameObject prefab)
        {
            if (TryGetEntry(key, out Entry entry) && entry.m_Prefab != null)
            {
                prefab = entry.m_Prefab;
                return true;
            }

            prefab = null;
            return false;
        }

        /// <summary>
        /// 构建/重建查找表（重复键首到先得并记告警——注册数据错误不应静默换绑）。
        /// </summary>
        private void EnsureLookup()
        {
            if (_keyToEntry != null)
            {
                return;
            }

            _keyToEntry = new Dictionary<string, Entry>(m_Entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry entry = m_Entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.m_Key))
                {
                    continue;
                }

                if (_keyToEntry.ContainsKey(entry.m_Key))
                {
                    LogUtility.Warning("[SaveService] SavePrefabRegistry '{0}' has duplicate key '{1}' — first entry wins.", name, entry.m_Key);
                    continue;
                }

                _keyToEntry.Add(entry.m_Key, entry);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器期条目变更后失效查找表（下次查询重建）。
        /// </summary>
        private void OnValidate()
        {
            _keyToEntry = null;
        }
#endif
    }
}
