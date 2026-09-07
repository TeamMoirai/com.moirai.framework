using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Attributes.Editor.Drawers
{
    /// <summary>
    /// <see cref="DisableAttribute"/> 绘制器：在运行时或编辑期按配置将字段绘制为禁用（只读）状态。
    /// </summary>
    [CustomPropertyDrawer(typeof(DisableAttribute), true)]
    public class DisableAttributeDrawer : PropertyDrawer
    {
        // 缓存的特性引用，首次绘制时延迟获取。
        private DisableAttribute _target;

        // 必须沿用属性自身的高度，否则部分属性会因折叠而小于其内容的实际高度。
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        // 绘制禁用状态的属性字段。
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            _target ??= attribute as DisableAttribute;

            if (EditorApplication.isPlaying && _target.EditorMode.HasFlag(DisableAttribute.EMode.Play) ||
                !EditorApplication.isPlaying && _target.EditorMode.HasFlag(DisableAttribute.EMode.Edit))
            {
                GUI.enabled = false; // 禁用字段
            }
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true; // 启用字段
        }
    }
}