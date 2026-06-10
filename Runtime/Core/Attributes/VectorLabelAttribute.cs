using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 将 Vector 的 XY(ZW) 轴分别显示为指定的 Label
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class VectorLabelAttribute : PropertyAttribute
    {
        public readonly string[] Labels;

        public VectorLabelAttribute(params string[] labels)
        {
            Labels = labels;
        }
    }
}