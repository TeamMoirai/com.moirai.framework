using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 排序图层特性：在 Inspector 中为字段提供 Unity 排序图层（Sorting Layer）下拉选择。
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class SortingLayerAttribute : PropertyAttribute { }
}