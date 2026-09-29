using System;
using System.Diagnostics;

namespace Sirenix.OdinInspector
{
    /// <summary>
    /// 与 <see cref="InfoBoxAttribute"/> 相同，但只显示提示，不显示字段、属性。
    /// </summary>
    [DontApplyToListElements]
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public class HelpInfoAttribute : Attribute
    {
        /// <summary>要显示在信息框中的消息。</summary>
        public string Message;
        
        /// <summary>
        /// 可选：用于显示和隐藏信息框的成员字段、属性或函数。
        /// </summary>
        public string VisibleIf;

        /// <summary>
        /// 为 <c>true</c> 时，InfoBox 会忽略 GUI.enable 标志，始终按启用状态绘制。
        /// </summary>
        public bool GUIAlwaysEnabled;

        /// <summary>
        /// 支持的着色格式：命名色、十六进制、RGBA/RGB，以及 Odin 属性表达式（如 <c>@this.MyColor</c>）。
        /// </summary>
        /// <remarks>
        /// 可用命名色：black、blue、clear、cyan、gray、green、grey、magenta、orange、purple、red、transparent、transparentBlack、transparentWhite、 <br />
        /// white、yellow。<br />
        /// 浅色系：lightblue、lightcyan、lightgray、lightgreen、lightgrey、lightmagenta、lightorange、lightpurple、lightred、lightyellow。<br />
        /// 深色系：darkblue、darkcyan、darkgray、darkgreen、darkgrey、darkmagenta、darkorange、darkpurple、darkred、darkyellow。
        /// </remarks>
        public string IconColor;

        private SdfIconType icon;

        /// <summary>显示在消息旁边的图标。</summary>
        public SdfIconType Icon
        {
          get => this.icon;
          set
          {
            this.icon = value;
            this.HasDefinedIcon = true;
          }
        }

        public bool HasDefinedIcon { get; private set; }

        /// <summary>在属性上方显示信息框。</summary>
        /// <param name="message">消息框的消息。支持引用成员字符串字段、属性或方法， 通过 $ 引用。</param>
        /// <param name="visibleIfMemberName">用于显示或隐藏消息框的 bool 成员名称。</param>
        public HelpInfoAttribute(string message, string visibleIfMemberName = null)
        {
          this.Message = message;
          this.VisibleIf = visibleIfMemberName;
        }
        
        /// <summary>在属性上方显示信息框。</summary>
        /// <param name="message">消息框的消息。支持引用成员字符串字段、属性或方法， 通过 $ 引用。</param>
        /// <param name="icon">显示在消息旁边的图标。</param>
        /// <param name="visibleIfMemberName">用于显示或隐藏消息框的 bool 成员名称。</param>
        public HelpInfoAttribute(string message, SdfIconType icon, string visibleIfMemberName = null)
        {
          this.Message = message;
          this.Icon = icon;
          this.VisibleIf = visibleIfMemberName;
        }
  }
}