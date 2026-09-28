using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 自定义标签特性：在 Inspector 中将目标字段的显示名替换为指定文本。
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class CustomLabelAttribute : PropertyAttribute
    {
        /// <summary>
        /// 字段在 Inspector 中显示的自定义标签文本。
        /// </summary>
        public string label;
        /// <summary>
        /// 创建自定义标签特性实例。
        /// </summary>
        /// <param name="label">字段在 Inspector 中显示的自定义标签文本。</param>
        public CustomLabelAttribute(string label)
        {
            this.label = label;
        }
    }
}