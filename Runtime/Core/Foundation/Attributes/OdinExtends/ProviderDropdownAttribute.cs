using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 为 <see cref="SerializeReference"/> 字段或类型名字段提供实现类下拉菜单。
    /// </summary>
    /// <remarks>
    /// 引用模式（推荐）：配合 <see cref="SerializeReference"/>，字段为抽象类，选中后直接存实例并展开编辑子字段。 <br />
    /// 类型名模式：字段为 <c>string</c>，存类型全名，运行时经 <c>ReflectionUtility.ResolveImplType&lt;T&gt;</c> 创建实例，适用于接口类型。
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

        /// <summary>
        /// 下拉是否显示 "(None)" 项，默认 <c>false</c>。
        /// </summary>
        /// <remarks>基类下无任何可选派生类时强制显示 "(None)"，避免空下拉。</remarks>
        public bool ShowNone { get; }

        /// <param name="baseType">基类类型，用于搜索所有派生类。null 时从字段类型推断。</param>
        /// <param name="label">可选的下拉框显示名称。</param>
        /// <param name="showNone">
        /// 是否显示 "(None)" 项（默认 <c>false</c>）。<c>true</c> 时显示，除非无任何可选候选类型。
        /// </param>
        public ProviderDropdownAttribute(Type baseType = null, string label = null, bool showNone = false)
        {
            BaseType = baseType;
            Label = label;
            ShowNone = showNone;
        }
    }
}
