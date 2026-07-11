using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 为辅助基类提供实现类下拉菜单选择指定接口的所有实现类的类型全名。<br/>
    /// 字段建议命名为 <c>m_XXHelperTypeName</c>
    /// </summary>
    /// <remarks>
    /// 自 Unity 2019.3 起，推荐使用 <see cref="UnityEngine.SerializeReference"/> 特性配合抽象基类来实现多态序列化，
    /// 无需手动管理类型选择与实例化，配合 <see cref="ReferenceDropdownAttribute"/> 提供下拉。
    /// </remarks>
    /// <example>
    /// <code>
    /// [HelperDropdown(typeof(ICustomHelper))]
    /// [SerializeField] private string m_CustomHelperTypeName;
    /// </code>
    ///
    /// 获取实现类实例
    /// <code><![CDATA[
    /// var instance = FrameworkSettings.ResolveTypeOption<ICustomHelper>(m_CustomHelperTypeName);
    /// ]]></code>
    /// </example>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field)]
    public class HelperDropdownAttribute : PropertyAttribute
    {
        /// <summary>要搜索的辅助类基类类型</summary>
        public Type BaseType { get; }

        /// <summary>可选的下拉框标签覆写，为空时从字段名自动推导</summary>
        public string Label { get; }

        /// <param name="baseType">辅助类的基类类型，用于搜索所有派生类</param>
        /// <param name="label">可选的下拉框显示名称</param>
        public HelperDropdownAttribute(Type baseType, string label = null)
        {
            BaseType = baseType;
            Label = label;
        }
    }
}