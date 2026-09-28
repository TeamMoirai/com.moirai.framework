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
        /// A resolved string that defines the action to perform when the button is clicked, such as an expression or method invocation.
        /// </summary>
        public string Action;
        /// <summary>Optional label of the button.</summary>
        public string Label;
        /// <summary>
        /// Optional resolved string that specifies a condition for whether to show the inline button or not.
        /// </summary>
        public string ShowIf;
        /// <summary>Supports a variety of color formats, including named colors (e.g. "red", "orange", "green", "blue"), hex codes (e.g. "#FF0000" and "#FF0000FF"), and RGBA (e.g. "RGBA(1,1,1,1)") or RGB (e.g. "RGB(1,1,1)"), including Odin attribute expressions (e.g "@this.MyColor"). Here are the available named colors: black, blue, clear, cyan, gray, green, grey, magenta, orange, purple, red, transparent, transparentBlack, transparentWhite, white, yellow, lightblue, lightcyan, lightgray, lightgreen, lightgrey, lightmagenta, lightorange, lightpurple, lightred, lightyellow, darkblue, darkcyan, darkgray, darkgreen, darkgrey, darkmagenta, darkorange, darkpurple, darkred, darkyellow. </summary>
        public string ButtonColor;
        /// <summary>Supports a variety of color formats, including named colors (e.g. "red", "orange", "green", "blue"), hex codes (e.g. "#FF0000" and "#FF0000FF"), and RGBA (e.g. "RGBA(1,1,1,1)") or RGB (e.g. "RGB(1,1,1)"), including Odin attribute expressions (e.g "@this.MyColor"). Here are the available named colors: black, blue, clear, cyan, gray, green, grey, magenta, orange, purple, red, transparent, transparentBlack, transparentWhite, white, yellow, lightblue, lightcyan, lightgray, lightgreen, lightgrey, lightmagenta, lightorange, lightpurple, lightred, lightyellow, darkblue, darkcyan, darkgray, darkgreen, darkgrey, darkmagenta, darkorange, darkpurple, darkred, darkyellow. </summary>
        public string TextColor;
        public SdfIconType Icon;
        public IconAlignment IconAlignment;

        /// <summary>Draws a button to the right of the property.</summary>
        /// <param name="action">A resolved string that defines the action to perform when the button is clicked, such as an expression or method invocation.</param>
        /// <param name="label">Optional label of the button.</param>
        public InlineEnableButtonAttribute(string action, string label = null)
        {
          this.Action = action;
          this.Label = label;
        }

        /// <summary>Draws a button to the right of the property.</summary>
        /// <param name="action">A resolved string that defines the action to perform when the button is clicked, such as an expression or method invocation.</param>
        /// <param name="icon">The icon to be shown inside the button.</param>
        /// <param name="label">Optional label of the button.</param>
        public InlineEnableButtonAttribute(string action, SdfIconType icon, string label = null)
        {
          this.Action = action;
          this.Icon = icon;
          this.Label = label;
        }
    }
}