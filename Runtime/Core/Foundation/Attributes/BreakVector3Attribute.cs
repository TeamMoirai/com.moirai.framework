using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 将 Vector3 拆分为 3 个单独的属性显示
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class BreakVector3Attribute : PropertyAttribute 
    {
        public readonly string XLabel;
        public readonly string YLabel;
        public readonly string ZLabel;

        public BreakVector3Attribute(string xLabel, string yLabel, string zLabel)
        {
            XLabel = xLabel;
            YLabel = yLabel;
            ZLabel = zLabel;
        }
    }
}
