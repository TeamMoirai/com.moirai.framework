using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 为 <see cref="SerializeReference"/> 字段或 <see cref="string"/> 类型名字段提供实现类下拉菜单。<br/>
    /// 自动列出字段声明类型（或 <see cref="BaseType"/>）的所有非抽象派生类。
    /// </summary>
    /// <remarks>
    /// 两种使用模式：<br/>
    /// 1. <b>引用模式</b>（推荐）：配合 <see cref="SerializeReference"/> 使用，字段类型为抽象类，
    ///    下拉选择后直接存储实例，展开可编辑子字段。<br/>
    /// 2. <b>类型名模式</b>：字段为 <c>string</c>，存储类型全名，
    ///    运行时通过 <c>FrameworkSettings.ResolveTypeOption&lt;T&gt;(typeName)</c> 创建实例。
    ///    适用于接口类型（无法直接序列化实例的场景）。
    /// </remarks>
    /// <example>
    /// 引用模式：
    /// <code>
    /// [ProviderDropdown]
    /// [SerializeReference] private CustomHandler m_CustomHandler = new DefaultCustomHandler();
    /// </code>
    /// 类型名模式：
    /// <code>
    /// [ProviderDropdown(typeof(ICustomHelper), "Custom Helper")]
    /// [SerializeField] private string m_CustomHelperTypeName;
    /// </code>
    /// </example>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ProviderDropdownAttribute : PropertyAttribute
    {
        /// <summary>
        /// 要搜索的基类类型。为 null 时从字段类型自动推断（引用模式）。
        /// </summary>
        public Type BaseType { get; }

        /// <summary>
        /// 可选的下拉框标签覆写。为空时从字段名自动推导。
        /// </summary>
        public string Label { get; }

        /// <param name="baseType">基类类型，用于搜索所有派生类。null 时从字段类型推断。</param>
        /// <param name="label">可选的下拉框显示名称。</param>
        public ProviderDropdownAttribute(Type baseType = null, string label = null)
        {
            BaseType = baseType;
            Label = label;
        }
    }
}
