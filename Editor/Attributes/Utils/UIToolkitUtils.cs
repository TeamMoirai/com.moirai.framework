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
    public class UIToolkitUtils
    {
#if UNITY_2021_3_OR_NEWER

        public class DropdownButtonField : BaseField<string>
        {
            public readonly Button ButtonElement;
            public readonly Label ButtonLabelElement;
            // private readonly MethodInfo AlignLabel;

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

        public static IEnumerable<VisualElement> FindParentClass(VisualElement element, string className)
        {
            return IterUpWithSelf(element).Where(each => each.ClassListContains(className));
        }

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
