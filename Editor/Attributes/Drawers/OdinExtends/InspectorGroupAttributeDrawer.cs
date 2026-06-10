using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace Sirenix.OdinInspector.Editor.Drawers
{
    /// <summary>
    /// 参考 <see cref="FoldoutGroupAttributeDrawer"/>
    /// </summary>
    // ReSharper disable once UnusedType.Global
    public class InspectorGroupAttributeDrawer : OdinGroupDrawer<InspectorGroupAttribute>
    {
        private ValueResolver<string> titleGetter;

        protected override void Initialize()
        {
            #region 原方法

            this.titleGetter = ValueResolver.GetForString(this.Property, this.Attribute.GroupName);
            if (!this.Attribute.HasDefinedExpanded)
                return;
            this.Property.State.Expanded = this.Attribute.Expanded;

            #endregion
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            GUIHelper.PushColor(Attribute.Color);

            InspectorProperty property = this.Property;
            FoldoutGroupAttribute attribute = this.Attribute;
            if (this.titleGetter.HasError)
              SirenixEditorGUI.ErrorMessageBox(this.titleGetter.ErrorMessage);
            SirenixEditorGUI.BeginBox();
            SirenixEditorGUI.BeginBoxHeader();

            GUIHelper.PopColor(); // 弹出颜色，避免绘制内容区域颜色过深

            #region 原方法

            this.Property.State.Expanded = SirenixEditorGUI.Foldout(this.Property.State.Expanded, GUIHelper.TempContent(this.titleGetter.HasError ? property.Label.text : this.titleGetter.GetValue()));
            SirenixEditorGUI.EndBoxHeader();
            if (SirenixEditorGUI.BeginFadeGroup((object) this, this.Property.State.Expanded))
            {
              for (int index = 0; index < property.Children.Count; ++index)
              {
                InspectorProperty child = property.Children[index];
                child.Draw(child.Label);
              }
            }
            SirenixEditorGUI.EndFadeGroup();
            SirenixEditorGUI.EndBox();

            #endregion

            // 绘制侧边颜色
            Rect iconRect = GUILayoutUtility.GetLastRect();
            iconRect.width = 16;
            Rect leftBorderRect = new Rect(iconRect.xMin, iconRect.yMin, 2f, iconRect.height)
            {
                xMin = 12f,
                xMax = 15f
            };
            EditorGUI.DrawRect(leftBorderRect, Attribute.Color);
        }
    }
}