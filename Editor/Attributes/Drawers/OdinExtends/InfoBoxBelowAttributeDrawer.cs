using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Sirenix.OdinInspector.Editor.Drawers
{
    /// <summary>
    /// 参考 <see cref="InfoBoxAttributeDrawer"/>
    /// </summary>
    [DrawerPriority(0.0, 10001.0, 0.0)]
    // ReSharper disable once UnusedType.Global
    public class InfoBoxBelowAttributeDrawer : OdinAttributeDrawer<InfoBoxBelowAttribute>
    {
        private bool drawMessageBox;
        private ValueResolver<bool> visibleIfResolver;
        private ValueResolver<string> messageResolver;
        private ValueResolver<Color> iconColorResolver;
        private MessageType messageType;

        protected override void Initialize()
        {
            this.visibleIfResolver = ValueResolver.Get<bool>(this.Property, this.Attribute.VisibleIf, true);
            this.messageResolver = ValueResolver.GetForString(this.Property, this.Attribute.Message);
            this.iconColorResolver = ValueResolver.Get<Color>(this.Property, this.Attribute.IconColor, EditorStyles.label.normal.textColor);
            this.drawMessageBox = this.visibleIfResolver.GetValue();
            switch (this.Attribute.InfoMessageType)
            {
                case InfoMessageType.Info:
                    this.messageType = MessageType.Info;
                    break;
                case InfoMessageType.Warning:
                    this.messageType = MessageType.Warning;
                    break;
                case InfoMessageType.Error:
                    this.messageType = MessageType.Error;
                    break;
                default:
                    this.messageType = MessageType.None;
                    break;
            }
        }

        /// <summary>Draws the property.</summary>
        protected override void DrawPropertyLayout(GUIContent label)
        {
            // 先绘制属性
            // this.CallNextDrawer(label);

            // 再绘制 InfoBox
            bool flag = true;
            if (this.visibleIfResolver.HasError)
            {
                SirenixEditorGUI.ErrorMessageBox(this.visibleIfResolver.ErrorMessage);
                flag = false;
            }
            if (this.messageResolver.HasError)
            {
                SirenixEditorGUI.ErrorMessageBox(this.messageResolver.ErrorMessage);
                flag = false;
            }
            if (this.iconColorResolver.HasError)
            {
                SirenixEditorGUI.ErrorMessageBox(this.iconColorResolver.ErrorMessage);
                flag = false;
            }
            if (!flag)
            {
                // this.CallNextDrawer(label);
            }
            else
            {
                if (this.Attribute.GUIAlwaysEnabled)
                    GUIHelper.PushGUIEnabled(true);
                if (Event.current.type == UnityEngine.EventType.Layout)
                    this.drawMessageBox = this.visibleIfResolver.GetValue();
                if (this.drawMessageBox)
                {
                    string message = this.messageResolver.GetValue();
                    if (this.Attribute.HasDefinedIcon)
                        SirenixEditorGUI.IconMessageBox(message, this.Attribute.Icon, new Color?(this.iconColorResolver.GetValue()));
                    else
                        SirenixEditorGUI.MessageBox(message, this.messageType);
                }
                if (this.Attribute.GUIAlwaysEnabled)
                    GUIHelper.PopGUIEnabled();
                this.CallNextDrawer(label);
            }
        }
    }
}