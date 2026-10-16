using System;
using System.Diagnostics;

namespace Moirai.Atropos
{
    /// <summary>
    /// 标注在已编译但当前不可用的后端实现类上：下拉据此把该行转为灰显并把原因显示在详情面。
    /// </summary>
    /// <remarks>
    /// 供插件装配里的类型标注，按《公开面收敛》三分法取 `public` 且留在命名空间级；
    /// <c>[Conditional]</c> 使构建期不残留标注应用，运行期零足迹。<br />
    /// 与 <see cref="ProviderDisplayAttribute"/> 互补：后者给可选行的显示元数据，本特性给不可选行的原因。
    /// </remarks>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ProviderUnavailableAttribute : Attribute
    {
        /// <summary>灰显原因，须写清缺什么、怎么补。</summary>
        public string Reason { get; }

        /// <param name="reason">灰显原因，空值拒——全空的原因没有可显示的信息。</param>
        public ProviderUnavailableAttribute(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason 必须写清缺什么、怎么补：全空的自述没有可显示的信息");

            Reason = reason;
        }
    }
}
