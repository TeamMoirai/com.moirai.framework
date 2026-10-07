using System;
using System.Diagnostics;

namespace Moirai.Atropos
{
    /// <summary>
    /// 为实现类提供下拉菜单显示元数据，配合 <see cref="ProviderDropdownAttribute"/> 的候选列表展示。
    /// </summary>
    /// <remarks>
    /// 标注在候选实现类上（<c>[ProviderDropdown]</c> 字段的派生类）： <br />
    /// <c>Title</c> 非空时替换下拉行与选中态的类型名显示； <br />
    /// <c>Description</c> 非空时在下拉详情面板置顶优先显示（支持换行，超宽自动折行），无描述时面板回退显示 Type / Base / Assembly。 <br />
    /// 编辑器专用（<c>[Conditional]</c> 剥离构建期的标注应用），运行时无任何足迹。
    /// </remarks>
    /// <example>
    /// <code>
    /// [ProviderDisplay(title: "AES 加密", description: "档体加密 + 密钥经 KeyProvider 注入\n上线推荐")]
    /// internal class AESEncryptedSaveHandler : SaveServiceHandler { }
    /// </code>
    /// </example>
    [Conditional("UNITY_EDITOR")]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ProviderDisplayAttribute : Attribute
    {
        /// <summary>下拉行显示名。为 null 时显示类型名。</summary>
        public string Title { get; }

        /// <summary>下拉详情面板置顶描述。为 null 时面板回退显示 Type / Base / Assembly。</summary>
        public string Description { get; }

        /// <param name="title">下拉行显示名，null 时保留类型名。</param>
        /// <param name="description">下拉详情描述，null 时不占面板行。</param>
        public ProviderDisplayAttribute(string title = null, string description = null)
        {
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(description))
                throw new ArgumentException("Title 与 Description 至少给一项：全空的 [ProviderDisplay] 没有可显示的元数据");

            Title = title;
            Description = description;
        }
    }
}
