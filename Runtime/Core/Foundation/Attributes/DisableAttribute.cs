using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Attributes
{
    /// <summary>
    /// 其实就是脱离 Odin 的 ReadOnly 实现
    /// </summary>
    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class DisableAttribute : PropertyAttribute
    {
        [Flags]
        public enum EMode
        {
            Edit = 1,
            Play = 1 << 1,
        }

        public EMode EditorMode { get; }

        public DisableAttribute(EMode editorMode = EMode.Edit | EMode.Play)
        {
            EditorMode = editorMode;
        }
    }

    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class DisableInEditAttribute : DisableAttribute
    {
        public DisableInEditAttribute() : base(EMode.Edit)
        {
        }
    }

    [Conditional("UNITY_EDITOR")]
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
     public class DisableInPlayAttribute : DisableAttribute
    {
        public DisableInPlayAttribute() : base(EMode.Play)
        {
        }
    }
}