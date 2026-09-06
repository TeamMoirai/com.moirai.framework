using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    public sealed partial class YooAssetHandler
    {
        /// <summary>
        /// 为 <see cref="string"/> 类型资源包裹名字段提供 YooAsset 收集器包裹下拉菜单。<br/>
        /// 选项在每次绘制时实时读取 YooAsset 收集器设置（BundleCollectorSettingData）中已配置的包裹名。
        /// </summary>
        /// <remarks>
        /// 选项数据来自 YooAsset.Editor 程序集，绘制器位于 <c>Moirai.Atropos.Editor</c>（Runtime 程序集
        /// 不可引用编辑器程序集），运行时本特性为纯标记、无任何开销。
        /// </remarks>
        /// <example>
        /// <code>
        /// [CollectorPackageDropdown]
        /// [SerializeField] private string m_PackageName = "DefaultPackage";
        /// </code>
        /// </example>
        [Conditional("UNITY_EDITOR")]
        [AttributeUsage(AttributeTargets.Field)]
        internal sealed class CollectorPackageDropdownAttribute : PropertyAttribute
        {
        }
    }
}