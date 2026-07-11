#if UNITY_2019_3_OR_NEWER && !ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos
{
    [CustomPropertyDrawer(typeof(ReferenceDropdownAttribute), true)]
    internal sealed class ReferenceDropdownAttributeDrawer : PropertyDrawer
    {
        private const float PAD = 3f;
        private static readonly Dictionary<string, bool> s_Foldouts = new Dictionary<string, bool>();

        private Type[] _types;
        private GUIContent[] _names;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            EnsureBuilt();
            float h = EditorGUIUtility.singleLineHeight;

            if (property.managedReferenceValue != null && HasVisibleChildren(property))
            {
                string foldKey = property.propertyPath;
                if (!s_Foldouts.ContainsKey(foldKey))
                    s_Foldouts[foldKey] = true;

                if (s_Foldouts[foldKey])
                {
                    float spacing = EditorGUIUtility.standardVerticalSpacing;
                    h += spacing;

                    var child = property.Copy();
                    var end = child.GetEndProperty();
                    if (child.NextVisible(true))
                    {
                        h += PAD;
                        bool first = true;
                        while (!SerializedProperty.EqualContents(child, end))
                        {
                            if (!first) h += spacing;
                            h += EditorGUI.GetPropertyHeight(child, true);
                            first = false;
                            if (!child.NextVisible(false)) break;
                        }
                        h += PAD;
                    }
                }
            }

            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureBuilt();

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float lineH = EditorGUIUtility.singleLineHeight;

            int currentIndex = FindCurrentIndex(property);
            bool hasChildren = property.managedReferenceValue != null && HasVisibleChildren(property);

            if (currentIndex == 0 && property.managedReferenceValue == null)
            {
                // ── No type selected: standard label + dropdown ──
                int newIndex = EditorGUI.Popup(position, label, currentIndex, _names);
                if (newIndex != currentIndex)
                {
                    property.managedReferenceValue = Activator.CreateInstance(_types[newIndex - 1]);
                    GUI.changed = true;
                }
                return;
            }

            if (!hasChildren)
            {
                // ── No serializable children: standard label + dropdown ──
                int newIndex = EditorGUI.Popup(position, label, currentIndex, _names);
                if (newIndex != currentIndex)
                {
                    if (newIndex == 0)
                        property.managedReferenceValue = null;
                    else
                        property.managedReferenceValue = Activator.CreateInstance(_types[newIndex - 1]);
                    GUI.changed = true;
                }
                return;
            }

            // ── Has children: foldout header + dropdown ──
            string foldKey = property.propertyPath;
            if (!s_Foldouts.ContainsKey(foldKey))
                s_Foldouts[foldKey] = true;

            var headerRect = new Rect(position.x, position.y, position.width, lineH);
            var fieldRect = EditorGUI.PrefixLabel(headerRect, label);

            // Foldout only in the label area (left of field)
            var foldoutRect = new Rect(headerRect.x, headerRect.y,
                fieldRect.x - headerRect.x, lineH);
            s_Foldouts[foldKey] = EditorGUI.Foldout(foldoutRect, s_Foldouts[foldKey], GUIContent.none, true);

            // Dropdown in the standard field position
            int newIndex2 = EditorGUI.Popup(fieldRect, GUIContent.none, currentIndex, _names);

            if (newIndex2 != currentIndex)
            {
                if (newIndex2 == 0)
                    property.managedReferenceValue = null;
                else
                    property.managedReferenceValue = Activator.CreateInstance(_types[newIndex2 - 1]);
                GUI.changed = true;
            }

            if (!s_Foldouts[foldKey]) return;

            // ── Child properties inside a box ──
            float childStartY = position.y + lineH + spacing;
            float boxH = position.yMax - childStartY + PAD;
            GUI.Box(new Rect(position.x, childStartY, position.width, boxH), GUIContent.none);

            float y = childStartY + PAD;
            var child = property.Copy();
            var end = child.GetEndProperty();

            if (child.NextVisible(true))
            {
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel++;
                bool first = true;

                while (!SerializedProperty.EqualContents(child, end))
                {
                    if (!first) y += spacing;
                    float ch = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(
                        new Rect(position.x + PAD, y, position.width - PAD * 2, ch),
                        child, true);
                    y += ch;
                    first = false;
                    if (!child.NextVisible(false)) break;
                }

                EditorGUI.indentLevel = indent;
            }
        }

        private void EnsureBuilt()
        {
            if (_types != null) return;

            var baseType = fieldInfo.FieldType;

            _types = TypeCache
                .GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract && !t.Assembly.GetName().Name.EndsWith(".Tests"))
                .ToArray();

            Array.Sort(_types, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            _names = new GUIContent[_types.Length + 1];
            _names[0] = new GUIContent("(None)");
            for (int i = 0; i < _types.Length; i++)
                _names[i + 1] = new GUIContent($"{_types[i].Name}  ({_types[i].FullName})");
        }

        private int FindCurrentIndex(SerializedProperty property)
        {
            if (property.managedReferenceValue == null) return 0;

            var currentType = property.managedReferenceValue.GetType();
            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i] == currentType) return i + 1;
            }
            return 0;
        }

        private bool IsExpanded(string path)
        {
            return s_Foldouts.TryGetValue(path, out bool v) && v;
        }

        private static bool HasVisibleChildren(SerializedProperty property)
        {
            var child = property.Copy();
            var end = child.GetEndProperty();
            return child.NextVisible(true) && !SerializedProperty.EqualContents(child, end);
        }
    }
}
#endif