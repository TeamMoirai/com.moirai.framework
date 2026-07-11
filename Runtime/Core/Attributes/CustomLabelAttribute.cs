using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class CustomLabelAttribute : PropertyAttribute
    {
        public string label;
        public CustomLabelAttribute(string label)
        {
            this.label = label;
        }
    }
}