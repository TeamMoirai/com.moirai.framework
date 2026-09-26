#if UNITY_2021_3_OR_NEWER
using System.Collections.Generic;
using UnityEditor;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
#endif

namespace Moirai.Atropos.Attributes.Editor.Utils
{
    /// <summary>
    /// UI Toolkit 辅助工具类。
    /// </summary>
    public class UIToolkitUtils
    {
#if UNITY_2021_3_OR_NEWER

        /// <summary>
        /// 带下拉按钮的字符串字段（基于 <see cref="BaseField{string}"/>），按钮文本通过内部 <see cref="Label"/> 展示。
        /// </summary>
        public class DropdownButtonField : BaseField<string>
        {
            /// <summary>下拉按钮元素。</summary>
            public readonly Button ButtonElement;
            /// <summary>按钮上显示文本的标签元素。</summary>
            public readonly Label ButtonLabelElement;
            // private readonly MethodInfo AlignLabel;

            /// <summary>
            /// 创建下拉按钮字段。
            /// </summary>
            /// <param name="label">字段标签文本。</param>
            /// <param name="visualInput">用作输入控件的按钮元素。</param>
            /// <param name="buttonLabel">按钮内显示文本的标签元素。</param>
            public DropdownButtonField(string label, Button visualInput, Label buttonLabel) : base(label, visualInput)
            {
                ButtonElement = visualInput;
                ButtonLabelElement = buttonLabel;

                // AlignLabel = typeof(BaseField<string>).GetMethod("AlignLabel", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            // public void AlignLabelForce()
            // {
            //     AlignLabel.Invoke(this, new object[]{});
            // }
        }

        /// <summary>
        /// 创建一个 UI Toolkit 下拉按钮字段：标签文本左对齐显示于按钮内，按钮右侧叠加下拉箭头图标。
        /// </summary>
        /// <param name="label">字段标签文本。</param>
        /// <returns>构建好的 <see cref="DropdownButtonField"/> 实例。</returns>
        public static DropdownButtonField MakeDropdownButtonUIToolkit(string label)
        {
            Button button = new Button
            {
                style =
                {
                    height = EditorGUIUtility.singleLineHeight,
                    flexGrow = 1,
                    flexShrink = 1,

                    paddingRight = 2,
                    marginRight = 0,
                    marginLeft = 0,
                    alignItems = Align.FlexStart,
                },
                // name = NameButtonField(property),
                // userData = metaInfo.SelectedIndex == -1
                //     ? null
                //     : metaInfo.DropdownListValue[metaInfo.SelectedIndex].Item2,
            };

            Label buttonLabel = new Label
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    // paddingRight = 20,
                    // textOverflow = TextOverflow.Ellipsis,
                    // unityOverflowClipBox = OverflowClipBox.PaddingBox,
                    overflow = Overflow.Hidden,
                    marginRight = 15,
                    unityTextAlign = TextAnchor.MiddleLeft,
                },
            };

            button.Add(buttonLabel);

            DropdownButtonField dropdownButtonField = new DropdownButtonField(label, button, buttonLabel)
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                },
            };

            // dropdownButtonField.AddToClassList("unity-base-field__aligned");
            dropdownButtonField.AddToClassList(BaseField<UnityEngine.Object>.alignedFieldUssClassName);

            dropdownButtonField.Add(new Image
            {
                image = Resources.Load<Texture2D>(AttributesStaticRef.Icon_Dropdown),
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    maxWidth = 12,
                    maxHeight = EditorGUIUtility.singleLineHeight,
                    position = Position.Absolute,
                    right = 4,
                },
            });

            return dropdownButtonField;
        }

        /// <summary>
        /// 从指定元素开始沿视觉树向上（含自身）迭代，返回包含指定 USS 类名的所有元素。
        /// </summary>
        /// <param name="element">起始元素。</param>
        /// <param name="className">要匹配的 USS 类名。</param>
        /// <returns>含该 USS 类名的元素枚举（从自身向根方向）。</returns>
        public static IEnumerable<VisualElement> FindParentClass(VisualElement element, string className)
        {
            return IterUpWithSelf(element).Where(each => each.ClassListContains(className));
        }

        /// <summary>
        /// 从指定元素开始沿视觉树向上迭代，依次产出该元素及其所有祖先元素。
        /// </summary>
        /// <param name="element">起始元素。</param>
        /// <returns>该元素及其全部祖先元素的枚举。</returns>
        public static IEnumerable<VisualElement> IterUpWithSelf(VisualElement element)
        {
            if(element == null)
            {
                yield break;
            }

            yield return element;

            foreach (VisualElement visualElement in IterUpWithSelf(element.parent))
            {
                yield return visualElement;
            }
        }
    }
#endif
}
