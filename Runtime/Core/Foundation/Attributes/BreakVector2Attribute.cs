using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 将 Vector2 拆分为 2 个单独的属性显示
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class BreakVector2Attribute : PropertyAttribute
    {
        public readonly string XLabel;
        public readonly string YLabel;

        public BreakVector2Attribute(string xLabel, string yLabel)
        {
            XLabel = xLabel;
            YLabel = yLabel;
        }
    }
}
