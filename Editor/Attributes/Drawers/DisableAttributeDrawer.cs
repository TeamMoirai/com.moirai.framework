using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    [CustomPropertyDrawer(typeof(DisableAttribute), true)]
    public class DisableAttributeDrawer : PropertyDrawer
    {
        private DisableAttribute _target;

        // Necessary since some properties tend to collapse smaller than their content
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        // Draw a disabled property field
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            _target ??= attribute as DisableAttribute;

            if (EditorApplication.isPlaying && _target.EditorMode.HasFlag(DisableAttribute.EMode.Play) ||
                !EditorApplication.isPlaying && _target.EditorMode.HasFlag(DisableAttribute.EMode.Edit))
            {
                GUI.enabled = false; // Disable fields
            }
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true; // Enable fields
        }
    }
}