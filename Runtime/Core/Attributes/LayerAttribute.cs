using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 选择单个图层的属性。
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class LayerAttribute : PropertyAttribute { }
}