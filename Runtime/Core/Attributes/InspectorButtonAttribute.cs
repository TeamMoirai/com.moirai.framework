using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 显示单个按钮并隐藏原字段。
    /// </summary>
    /// <remarks>不与其他 Attribute 混用。</remarks>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class InspectorButtonAttribute : PropertyAttribute
    {
        public readonly string methodName;
        public readonly string buttonLabel;

        public InspectorButtonAttribute(string methodName, string buttonLabel = null)
        {
            this.methodName = methodName;
            this.buttonLabel = buttonLabel ?? methodName;
        }
    }
}