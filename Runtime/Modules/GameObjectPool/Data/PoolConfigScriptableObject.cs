using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.GameObjectPool
{
    /// <summary>
    /// 池配置 ScriptableObject。
    /// </summary>
    [CreateAssetMenu(fileName = "GameObjectPoolConfig", menuName = "Moirai/PoolConfig", order = 10)]
    public sealed class PoolConfigScriptableObject : ScriptableObject
    {
        [Tooltip("池条目列表。")]
        [SerializeField] private List<PoolEntry> m_Entries = new List<PoolEntry>();

        /// <summary>
        /// 构建编译后的目录。
        /// </summary>
        /// <returns>编译后的池目录。</returns>
        internal PoolCompiledCatalog BuildCatalog()
        {
            Normalize();
            return PoolCompiledCatalog.Build(m_Entries);
        }

        /// <summary>
        /// 规范化所有条目。
        /// </summary>
        public void Normalize()
        {
            if (m_Entries == null)
            {
                m_Entries = new List<PoolEntry>();
                return;
            }

            for (int i = 0; i < m_Entries.Count; i++)
            {
                m_Entries[i]?.Normalize();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Normalize();
        }
#endif
    }
}
