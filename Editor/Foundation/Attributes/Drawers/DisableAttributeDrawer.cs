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

        // 沿用属性自身高度（含子字段）。wrap 型 Drawer 内调用时 Unity 会按默认 Drawer 链取高，
        // 不会与本 Drawer 形成无限重入；与 OnGUI 里 PropertyField 的绘制高度对齐。
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