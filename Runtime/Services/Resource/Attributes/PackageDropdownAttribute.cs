using System;
using System.Diagnostics;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 为 <see cref="string"/> 型资源包裹名提供 YooAsset 收集器包裹下拉：选项实时取自收集器（BundleCollectorSettingData）已配置的包裹名。
    /// </summary>
    /// <remarks>
    /// 选项数据来自 YooAsset.Editor 程序集，绘制器住 <c>Moirai.Atropos.Editor</c>（Runtime 程序集不可引用编辑器程序集），运行时本特性为纯标记、无开销； <br />
    /// 字段（Unity 序列化槽）与属性（Odin <c>[ShowInInspector]</c> 句柄，宿主自绘引用行的场景）皆可贴； <br />
    /// 显示行由 <c>PackagePopupRow</c> 统一构建，与 <c>AssetReference</c> 引用行同一处；后端是否消费包名轴由后端自报（处理器能力属性），不消费时不接管该行。
    /// </remarks>
    /// <example>
    /// <code>
    /// [PackageDropdown]
    /// [SerializeField] private string m_PackageName = "Default";
    ///
    /// [PackageDropdown(AllowEmpty = true)]
    /// [ShowInInspector] private string PackageHandle { get; set; }
    /// </code>
    /// </example>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ResourcePackageDropdownAttribute : PropertyAttribute
    {
        /// <summary>
        /// 顶部是否加「留空 · 默认资源包」状态项（选中即写回空串，运行时用后端配置的默认资源包）。
        /// 必填包名的配置字段保持 <c>false</c>；可留空的引用槽设 <c>true</c>。
        /// </summary>
        public bool AllowEmpty { get; set; }
    }
}
