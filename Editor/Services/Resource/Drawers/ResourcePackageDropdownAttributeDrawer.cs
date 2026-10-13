using Moirai.Atropos.Attributes.Editor.Utils;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Resource.Editor
{
    /// <summary>
    /// 为 <see cref="ResourcePackageDropdownAttribute"/> 提供绘制：单行「包名下拉」，显示行统一由 <see cref="ResourcePackagePopup"/> 构建，
    /// 清单经 <see cref="ResourcePackageBridge"/> 按域缓存。
    /// </summary>
    /// <remarks>
    /// 优先级 wrapper=10001，高于 Odin 默认 managed reference drawer 与 DrawWithUnity(10000)，保证 Odin 走本抽屉的 IMGUI 行绘制。<br />
    /// 残值：Unity 原生检视（含 Inspector 的 Debug 模式）无抽屉可套，那行退成普通文本框。
    /// </remarks>
    [DrawerPriority(0, 10001, 0)]
    internal sealed class ResourcePackageDropdownAttributeDrawer : OdinAttributeDrawer<ResourcePackageDropdownAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            // 后端不消费包名（Addressables 单隐式目录）时不接管，退回默认字符串绘制——此时按收集器画下拉是错的
            if (ResourcePackageBridge.GetPackageNames() == null)
            {
                CallNextDrawer(label);
                return;
            }

            bool allowEmpty = Attribute.AllowEmpty;

            SerializedProperty prop = Property.Tree.UnitySerializedObject?.FindProperty(Property.UnityPropertyPath);

            GUIContent rowLabel = label ?? GUIContent.none;
            Rect rowRect = EditorGUILayout.GetControlRect(
                true, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

            if (prop != null)
            {
                DrawSerializedPopup(rowRect, prop, rowLabel, allowEmpty);
                return;
            }

            if (!DrawValueEntryFallback(rowRect, rowLabel, allowEmpty))
                CallNextDrawer(label);
        }

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 有 Unity 序列化后端时：按 <see cref="SerializedProperty"/> 读写包裹名（写后 <c>ApplyModifiedProperties</c> 落盘并触发 OnValidate）。
        /// </summary>
        private static void DrawSerializedPopup(Rect position, SerializedProperty property, GUIContent label, bool allowEmpty)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label, "Use with string fields only.");
                return;
            }

            GUIContent[] displayed = ResourcePackagePopup.Build(property.stringValue, allowEmpty, out int index, out bool manifestEmpty);

            using (new EditorGUI.DisabledScope(manifestEmpty))
            {
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.Popup(position, label, index, displayed);
                if (EditorGUI.EndChangeCheck() && !manifestEmpty && selected >= 0 && selected < displayed.Length)
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Change Package Name");
                    property.stringValue = ResourcePackagePopup.Pick(displayed, allowEmpty, selected);
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        /// <summary>
        /// 无 SerializedProperty 时（纯 Odin 宿主、<c>[ShowInInspector]</c> 句柄属性）：用 Odin ValueEntry 读写包裹名，仍从收集器设置取选项。
        /// </summary>
        /// <remarks>
        /// 改值不能停在 <c>WeakSmartValue =</c> 后接 <c>Property.Update(true)</c>：赋值只落进 ValueEntry 的待写槽，
        /// <c>Update</c> 随即从源读回原值把待写槽冲掉（实测：句柄属性选完包名弹回「留空」）。
        /// 须逐格写 <c>Values</c> 再 <c>ApplyChanges()</c>（Odin 改值成例），宿主属性的 setter 才真被调用。
        /// </remarks>
        private bool DrawValueEntryFallback(Rect rowRect, GUIContent rowLabel, bool allowEmpty)
        {
            var valueEntry = Property.ValueEntry;
            if (valueEntry == null || valueEntry.TypeOfValue != typeof(string))
                return false;

            GUIContent[] displayed = ResourcePackagePopup.Build(valueEntry.WeakSmartValue as string, allowEmpty,
                out int index, out bool manifestEmpty);

            using (new EditorGUI.DisabledScope(manifestEmpty))
            {
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUI.Popup(rowRect, rowLabel, index, displayed);
                if (EditorGUI.EndChangeCheck() && !manifestEmpty && selected >= 0 && selected < displayed.Length)
                {
                    string picked = ResourcePackagePopup.Pick(displayed, allowEmpty, selected);
                    var values = valueEntry.WeakValues;
                    for (int i = 0; i < values.Count; i++)
                    {
                        values[i] = picked;
                    }

                    valueEntry.ApplyChanges();
                    GUI.changed = true;
                }
            }

            return true;
        }

        #endregion
    }
}
