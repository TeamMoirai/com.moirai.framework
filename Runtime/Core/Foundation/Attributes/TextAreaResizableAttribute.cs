using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 可调整大小文本域特性：在 Inspector 中将字符串字段渲染为可拖拽调整大小的多行文本域。
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class TextAreaResizableAttribute : PropertyAttribute { }
}