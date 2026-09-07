using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 标签特性：在 Inspector 中为字段提供 Unity 标签（Tag）下拉选择。
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class TagAttribute : PropertyAttribute { }
}