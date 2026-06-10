using System;
using System.Diagnostics;
using Moirai.Atropos;
using UnityEngine;

namespace Sirenix.OdinInspector
{
    /// <summary>
    /// 带颜色的分组，与 <see cref="FoldoutGroupAttribute"/> 相同功能
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    // ReSharper disable once ClassNeverInstantiated.Global
    public class InspectorGroupAttribute : FoldoutGroupAttribute
    {
        // ReSharper disable once InconsistentNaming
        public Color Color;
        
        public InspectorGroupAttribute(string path, int colorIndex = 136, float order = 0)
            : base(path, order)
        {
            Color = ColorsUtility.GetColorAt(colorIndex);
        }

        public InspectorGroupAttribute(string path, float r, float g, float b, float a = 1f, float order = 0)
            : base(path, order)
        {
            Color.r = r;
            Color.g = g;
            Color.b = b;
            Color.a = a;
        }

        protected override void CombineValuesWith(PropertyGroupAttribute other)
        {
            base.CombineValuesWith(other);

            var otherAttr = (InspectorGroupAttribute)other;
            
            Color = otherAttr.Color != Color.white ? otherAttr.Color : Color;
            Order = otherAttr.Order != 0 ? otherAttr.Order : Order;
        }
    }
}