using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(HelperDropdownAttribute), true)]
    internal sealed class HelperDropdownAttributeDrawer : PropertyDrawer
    {
        private Type[]   _types;        // 所有可用的派生类型
        private string[] _displayNames; // 下拉框显示名称列表
        private bool     _built;        // 是否已构建类型列表

        private HelperDropdownAttribute Attr => (HelperDropdownAttribute)attribute;

        /// <summary>
        /// 懒加载：首次绘制时通过 AssemblyUtility 收集所有非抽象的派生类型，
        /// 按名称排序后生成下拉选项数组。
        /// </summary>
        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _types = AssemblyUtility.GetRuntimeTypes(Attr.BaseType).ToArray();

            Array.Sort(_types, (a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            _displayNames = new string[_types.Length];
            for (int i = 0; i < _types.Length; i++)
                _displayNames[i] = _types[i].Name;
        }

        /// <summary>
        /// 根据当前存储的类型全名字符串，查找对应的下拉框索引。
        /// 未找到时返回 -1。
        /// </summary>
        private int FindCurrentIndex(SerializedProperty property)
        {
            string current = property.stringValue;
            if (string.IsNullOrEmpty(current))
                return -1;

            for (int i = 0; i < _types.Length; i++)
            {
                if (_types[i].FullName == current || _types[i].Name == current)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 获取人性化的显示名称。
        /// 优先使用特性中指定的 Label（原样返回）；否则从字段名自动推导并追加 " Helper"：
        ///   m_FooHelperTypeName → Foo Helper
        /// </summary>
        private string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(Attr.Label))
                return Attr.Label;

            string name = fieldInfo.Name;
            name = Regex.Replace(name, @"^m_", string.Empty);
            name = Regex.Replace(name, @"HelperTypeName$", string.Empty);
            name = Regex.Replace(name, @"((?<=[a-z])[A-Z]|[A-Z](?=[a-z]))", " $1").Trim();
            return name + " Helper";
        }

        // ═══ 计算属性高度 ═══════════════════════
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            EnsureBuilt();
            return EditorGUIUtility.singleLineHeight;
        }

        // ═══ 绘制 GUI ══════════════════════════
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureBuilt();

            int currentIndex = FindCurrentIndex(property);

            // ── 绘制类型选择下拉框 ──
            Rect popupRect = new Rect(
                position.x, position.y,
                position.width, EditorGUIUtility.singleLineHeight);

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(
                popupRect,
                GetDisplayName(),
                currentIndex,
                _displayNames);

            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = newIndex >= 0 && newIndex < _types.Length
                    ? _types[newIndex].FullName
                    : null;
            }
        }
    }
}