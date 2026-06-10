using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 替换 <see cref="Nullable{T}"/>，可在检查面板中进行编辑
    /// </summary>
    [Serializable]
    public struct Optional<T>
    {
        [SerializeField] private bool m_Enabled;

        [SerializeField] private T m_Value;

        public readonly bool Enabled => m_Enabled;

        public readonly T Value => m_Value;

        public Optional(T initialValue, bool isEnabled = true)
        {
            m_Enabled = isEnabled;
            m_Value = initialValue;
        }
    }
}