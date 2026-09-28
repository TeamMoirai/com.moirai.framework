using System;
using System.Diagnostics;

namespace Sirenix.OdinInspector
{
    /// <summary>
    /// 条件下拉框特性：<see cref="ValueDropdownAttribute"/> 的扩展，可额外指定是否显示内联下拉按钮的条件。
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public class ConditionDropdownAttribute : ValueDropdownAttribute
    {
        /// <summary>
        /// 可选的条件字符串（经成员名称解析求值），决定是否显示内联按钮。
        /// </summary>
        public string ShowIf;

        /// <summary>
        /// 创建条件下拉框特性实例。
        /// </summary>
        /// <param name="valuesGetter">返回下拉候选值集合的成员函数名称。</param>
        public ConditionDropdownAttribute(string valuesGetter) : base(valuesGetter)
        {
        }
    }
}