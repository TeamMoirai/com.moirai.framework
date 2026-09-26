using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 将 Bool 类型的字段显示为按钮组
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class BooleanButtonAttribute : PropertyAttribute
    {
        public readonly string Label = null;
        public readonly string FalseLabel;
        public readonly string TrueLabel;
        public readonly bool FalseLabelFirst;

        public BooleanButtonAttribute(string label, string falseLabel, string trueLabel, bool falseLabelFirst)
        {
            Label = label;
            FalseLabelFirst = falseLabelFirst;
            FalseLabel = falseLabel;
            TrueLabel = trueLabel;
        }
    }
}