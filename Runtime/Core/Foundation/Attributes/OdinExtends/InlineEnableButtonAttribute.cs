using System;
using System.Diagnostics;

namespace Sirenix.OdinInspector
{
    /// <summary>
    /// 与 <see cref="InlineButtonAttribute"/> 一样，但是按钮会始终可用，不受 <see cref="DisableIfAttribute"/> 影响。
    /// </summary>
    [DontApplyToListElements]
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public class InlineEnableButtonAttribute : Attribute
    {
        /// <summary>
        /// 经解析的字符串，定义点击按钮时执行的操作，例如表达式或方法 调用。
        /// </summary>
        public string Action;
        /// <summary>可选：按钮的标签。</summary>
        public string Label;
        /// <summary>
        /// 可选：指定是否显示内联按钮的经解析的字符串。
        /// </summary>
        public string ShowIf;
        /// <summary>
        /// 支持的着色格式：命名色、十六进制、RGBA/RGB，以及 Odin 属性表达式（如 <c>@this.MyColor</c>）。
        /// </summary>
        /// <remarks>
        /// 可用命名色：black、blue、clear、cyan、gray、green、grey、magenta、orange、purple、red、transparent、transparentBlack、transparentWhite、 <br />
        /// white、yellow。<br />
        /// 浅色系：lightblue、lightcyan、lightgray、lightgreen、lightgrey、lightmagenta、lightorange、lightpurple、lightred、lightyellow。<br />
        /// 深色系：darkblue、darkcyan、darkgray、darkgreen、darkgrey、darkmagenta、darkorange、darkpurple、darkred、darkyellow。
        /// </remarks>
        public string ButtonColor;
        /// <summary>
        /// 支持的着色格式：命名色、十六进制、RGBA/RGB，以及 Odin 属性表达式（如 <c>@this.MyColor</c>）。
        /// </summary>
        /// <remarks>
        /// 可用命名色：black、blue、clear、cyan、gray、green、grey、magenta、orange、purple、red、transparent、transparentBlack、transparentWhite、 <br />
        /// white、yellow。<br />
        /// 浅色系：lightblue、lightcyan、lightgray、lightgreen、lightgrey、lightmagenta、lightorange、lightpurple、lightred、lightyellow。<br />
        /// 深色系：darkblue、darkcyan、darkgray、darkgreen、darkgrey、darkmagenta、darkorange、darkpurple、darkred、darkyellow。
        /// </remarks>
        public string TextColor;
        public SdfIconType Icon;
        public IconAlignment IconAlignment;

        /// <summary>在属性右侧绘制按钮。</summary>
        /// <param name="action">经解析的字符串，定义点击按钮时执行的操作，例如 表达式或方法调用。</param>
        /// <param name="label">可选：按钮的标签。</param>
        public InlineEnableButtonAttribute(string action, string label = null)
        {
          this.Action = action;
          this.Label = label;
        }

        /// <summary>在属性右侧绘制按钮。</summary>
        /// <param name="action">经解析的字符串，定义点击按钮时执行的操作，例如 表达式或方法调用。</param>
        /// <param name="icon">显示在按钮内的图标。</param>
        /// <param name="label">可选：按钮的标签。</param>
        public InlineEnableButtonAttribute(string action, SdfIconType icon, string label = null)
        {
          this.Action = action;
          this.Icon = icon;
          this.Label = label;
        }
    }
}