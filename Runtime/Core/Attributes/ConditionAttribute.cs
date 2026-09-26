using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public class ConditionAttribute : PropertyAttribute
    {
        public enum ConditionType
        {
            IsTrue,
            IsFalse,

            IsEqualTo,
            IsNotEqualTo,
            IsGreaterThan,
            IsLessThan,

            IsNotNull,
            IsNull,
        }

        public enum VisibilityType
        {
            Hidden,
            NotEditable
        }

        public readonly string[] conditionPropertyNames;
        public readonly ConditionType[] conditionTypes;
        public readonly float[] values;
        public readonly VisibilityType visibilityType;


        /// <summary>
        /// 根据其他属性的条件来决定目标属性的可见性。
        /// </summary>
        /// <param name="conditionPropertyName">条件所使用的属性名称。</param>
        /// <param name="conditionType">条件类型。</param>
        /// <param name="conditionValue">条件的参数值。如果不需要（如 ConditionType.IsTrue/ConditionType.IsNotNull），可以置为 0。</param>
        /// <param name="visibilityType">若条件不满足时要执行的可见性操作。</param>
        /// <remarks>如果目标属性依赖于其他某个属性，可使用此特性。</remarks>
        public ConditionAttribute(string conditionPropertyName, ConditionType conditionType,
            float conditionValue, VisibilityType visibilityType = VisibilityType.Hidden)
        {
            this.conditionPropertyNames = new string[] { conditionPropertyName };
            this.conditionTypes = new ConditionType[] { conditionType };
            this.values = new float[] { conditionValue };
            this.visibilityType = visibilityType;
        }

        /// <summary>
        /// 根据其他属性的条件来决定目标属性的可见性。条件间为 AND 关系。
        /// </summary>
        /// <param name="conditionPropertyNames">条件所使用的属性名称。</param>
        /// <param name="conditionTypes">条件类型。</param>
        /// <param name="conditionValues">条件的参数值。如果不需要（如 ConditionType.IsTrue/ConditionType.IsNotNull），可以置为 0。</param>
        /// <param name="visibilityType">若条件不满足时要执行的可见性操作。</param>
        /// <remarks>如果目标属性依赖于其他某个属性，可使用此特性。</remarks>
        /// <remarks>如果目标属性依赖于其他某个属性，可使用此特性。</remarks>
        public ConditionAttribute(string[] conditionPropertyNames, ConditionType[] conditionTypes,
            float[] conditionValues, VisibilityType visibilityType = VisibilityType.Hidden)
        {
            this.conditionPropertyNames = conditionPropertyNames;
            this.conditionTypes = conditionTypes;
            this.values = conditionValues;
            this.visibilityType = visibilityType;
        }

        /// <summary>
        /// 简单的条件属性
        /// </summary>
        /// <param name="conditionBoolean">条件所使用的属性名称。</param>
        /// <param name="hideInInspector">是否显示在检查面板中。</param>
        /// <param name="negative"><c>true</c> 相当 HideIf，<c>false</c> 相当 ShowIf。</param>
        public ConditionAttribute(string conditionBoolean, bool hideInInspector, bool negative = false)
        {
            this.conditionPropertyNames = new string[] { conditionBoolean };
            this.conditionTypes = new ConditionType[] {negative ? ConditionType.IsFalse : ConditionType.IsTrue };
            this.values = new float[] { 0f };
            this.visibilityType = hideInInspector ? VisibilityType.Hidden : VisibilityType.NotEditable;
        }
    }
}