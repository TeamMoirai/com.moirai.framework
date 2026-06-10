using UnityEngine;

namespace Moirai.Atropos.Input
{
    [System.Serializable]
    public struct FloatAction
    {
        [SerializeField] private float m_Value;

        /// <summary>
        /// 动作的当前值。
        /// </summary>
        public float Value
        {
            get => m_Value;
            set => m_Value = value;
        }

        /// <summary>
        /// 重置该动作的值。
        /// </summary>
        public void Reset()
        {
            m_Value = 0f;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // EDITOR ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    
    [UnityEditor.CustomPropertyDrawer(typeof(FloatAction))]
    public class FloatActionEditor : UnityEditor.PropertyDrawer
    {
        public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
        {
            UnityEditor.EditorGUI.BeginProperty(position, label, property);

            // 查找 FloatAction 结构体中的 value 属性
            UnityEditor.SerializedProperty value = property.FindPropertyRelative("m_Value");

            // 设置标签的矩形区域
            Rect fieldRect = position;
            fieldRect.height = UnityEditor.EditorGUIUtility.singleLineHeight;
            fieldRect.width = 100;

            // 绘制属性的标签
            UnityEditor.EditorGUI.LabelField(fieldRect, label);

            // 移动矩形区域，为值的输入框腾出空间
            fieldRect.x += 110;

            // 绘制 value 属性的输入框，不显示额外的标签
            UnityEditor.EditorGUI.PropertyField(fieldRect, value, GUIContent.none);

            // 结束绘制属性
            UnityEditor.EditorGUI.EndProperty();
        }
    }

#endif
}