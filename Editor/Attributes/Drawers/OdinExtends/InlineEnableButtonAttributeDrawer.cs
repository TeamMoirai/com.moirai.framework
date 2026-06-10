using Sirenix.OdinInspector.Editor.ActionResolvers;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Sirenix.OdinInspector.Editor.Drawers
{
    /// <summary>
    /// 参考 <see cref="InlineButtonAttributeDrawer{T}"/>
    /// </summary>
    [DrawerPriority(DrawerPriorityLevel.WrapperPriority)]
    public class InlineEnableButtonAttributeDrawer<T> : OdinAttributeDrawer<InlineEnableButtonAttribute, T>
    {
        private ValueResolver<string> labelGetter;
        private ActionResolver clickAction;
        private ValueResolver<bool> showIfGetter;
        private ValueResolver<Color> buttonColorGetter;
        private ValueResolver<Color> textColorGetter;
        private bool show = true;
        private string tooltip;

        protected override void Initialize()
        {
            this.labelGetter = this.Attribute.Label == null ? ValueResolver.Get<string>(this.Property, (string) null, this.Attribute.Action.SplitPascalCase()) : ValueResolver.GetForString(this.Property, this.Attribute.Label);
            this.clickAction = ActionResolver.Get(this.Property, this.Attribute.Action);
            this.showIfGetter = ValueResolver.Get<bool>(this.Property, this.Attribute.ShowIf, true);
            this.buttonColorGetter = ValueResolver.Get<Color>(this.Property, this.Attribute.ButtonColor);
            this.textColorGetter = ValueResolver.Get<Color>(this.Property, this.Attribute.TextColor, SirenixGUIStyles.Button.normal.textColor);
            this.show = this.showIfGetter.GetValue();
            this.tooltip = this.Property.GetAttribute<PropertyTooltipAttribute>()?.Tooltip ?? this.Property.GetAttribute<TooltipAttribute>()?.tooltip;
        }

        /// <summary>Draws the property.</summary>
        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (this.labelGetter.HasError || this.clickAction.HasError || this.showIfGetter.HasError || this.buttonColorGetter.HasError || this.textColorGetter.HasError)
            {
                this.labelGetter.DrawError();
                this.clickAction.DrawError();
                this.buttonColorGetter.DrawError();
                this.textColorGetter.DrawError();
                this.CallNextDrawer(label);
            }
            else
            {
                if (Event.current.type == UnityEngine.EventType.Layout)
                    this.show = this.showIfGetter.GetValue();
                if (this.show)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.BeginVertical();
                    this.CallNextDrawer(label);
                    EditorGUILayout.EndVertical();
                    GUI.enabled = true; // 启用 GUI 事件
                    GUIContent guiContent = new GUIContent(this.labelGetter.GetValue(), this.tooltip);
                    float totalWidth;
                    SirenixEditorGUI.CalculateMinimumSDFIconButtonWidth(guiContent.text, (GUIStyle) null, this.Attribute.Icon != 0, EditorGUIUtility.singleLineHeight, out float _, out float _, out float _, out totalWidth);
                    Rect controlRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.MaxWidth(totalWidth));
                    Color buttonColor = this.buttonColorGetter.GetValue();
                    Color textColor = this.textColorGetter.GetValue();
                    if (SirenixEditorGUI.SDFIconButton(controlRect, guiContent, buttonColor, textColor, this.Attribute.Icon, this.Attribute.IconAlignment))
                        this.InvokeButton(guiContent);
                    EditorGUILayout.EndHorizontal();
                }
                else
                    this.CallNextDrawer(label);
            }
        }

        private void InvokeButton(GUIContent buttonLabel)
        {
            this.Property.RecordForUndo("Click " + buttonLabel?.ToString());
            this.clickAction.DoActionForAllSelectionIndices();
        }
    }
}