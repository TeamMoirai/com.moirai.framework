using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 显示一组按钮，并将原变量隐藏
    /// <remarks>不与其他 Attribute 混用</remarks>>
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = true)]
    public class InspectorButtonBarAttribute : PropertyAttribute
    {
        public string[] Labels { get; set; }
        public string[] Methods{ get; set; }
        public bool[] OnlyWhenPlaying{ get; set; }
        public string[] UssClass{ get; set; }
        
        public InspectorButtonBarAttribute(string[] labels, string[] methods, bool[] onlyWhenPlaying, string[] ussClass)
        {
            this.Labels = labels;
            this.Methods = methods;
            this.OnlyWhenPlaying = onlyWhenPlaying;
            this.UssClass = ussClass;
        }
    }
}