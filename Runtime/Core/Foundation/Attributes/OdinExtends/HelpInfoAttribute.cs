using System;
using System.Diagnostics;

namespace Sirenix.OdinInspector
{
    /// <summary>
    /// 与 <see cref="InfoBoxAttribute"/> 相同，但只显示提示，不显示字段、属性
    /// </summary>
    [DontApplyToListElements]
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public class HelpInfoAttribute : Attribute
    {
        /// <summary>The message to display in the info box.</summary>
        public string Message;
        
        /// <summary>
        /// Optional member field, property or function to show and hide the info box.
        /// </summary>
        public string VisibleIf;

        /// <summary>
        /// When <c>true</c> the InfoBox will ignore the GUI.enable flag and always draw as enabled.
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

        /// <summary>The icon to be displayed next to the message.</summary>
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

        /// <summary>Displays an info box above the property.</summary>
        /// <param name="message">The message for the message box. Supports referencing a member string field, property or method <br />
        /// by using $.</param>
        /// <param name="visibleIfMemberName">Name of member bool to show or hide the message box.</param>
        public HelpInfoAttribute(string message, string visibleIfMemberName = null)
        {
          this.Message = message;
          this.VisibleIf = visibleIfMemberName;
        }
        
        /// <summary>Displays an info box above the property.</summary>
        /// <param name="message">The message for the message box. Supports referencing a member string field, property or method <br />
        /// by using $.</param>
        /// <param name="icon">The icon to be displayed next to the message.</param>
        /// <param name="visibleIfMemberName">Name of member bool to show or hide the message box.</param>
        public HelpInfoAttribute(string message, SdfIconType icon, string visibleIfMemberName = null)
        {
          this.Message = message;
          this.Icon = icon;
          this.VisibleIf = visibleIfMemberName;
        }
  }
}