using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 为 [SerializeReference] 字段绘制类型选择下拉框，
    /// 自动列出字段声明类型的所有非抽象子类。
    /// </summary>
    /// <example>
    /// <code>
    /// [ReferenceDropdown]
    /// [SerializeReference] private CustomHandler m_CustomHandler = new DefaultCustomHandler();
    /// </code>
    /// </example>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ReferenceDropdownAttribute : PropertyAttribute { }
}