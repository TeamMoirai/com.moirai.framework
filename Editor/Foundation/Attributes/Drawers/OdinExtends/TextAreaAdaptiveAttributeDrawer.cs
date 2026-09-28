using System;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Sirenix.OdinInspector
{
    /// <summary>
    /// 多行文本块：高度随内容行数在 MinLines 与 MaxLines 之间自适应，超出 MaxLines 后由文本框自身滚动。
    /// 可作用于字符串字段与 <c>[ShowInInspector]</c> 属性。
    /// <para>Unity 的 <c>[TextArea]</c> / <c>[Multiline]</c> 在 Odin 下只对序列化字段生效，
    /// 属性会退化成单行标签；Odin 自带的 <c>[MultiLineProperty]</c> 又是固定行数。两者都不满足预览类界面。</para>
    /// </summary>
    /// <seealso cref="Sirenix.OdinInspector.Editor.Drawers.TextAreaAdaptiveAttributeDrawer" />
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    // ReSharper disable once UnusedType.Global
    public class TextAreaAdaptiveAttribute : Attribute
    {
        /// <summary>内容不足时的最小行数。</summary>
        public int MinLines;

        /// <summary>内容超出后的最大行数，此后文本框内部滚动。</summary>
        public int MaxLines;

        public TextAreaAdaptiveAttribute(int minLines = 1, int maxLines = 16)
        {
            MinLines = minLines;
            MaxLines = maxLines;
        }
    }
}

namespace Sirenix.OdinInspector.Editor.Drawers
{
    /// <summary>
    /// <see cref="TextAreaAdaptiveAttribute" /> 的 Drawer。不绘制 label——文本块要占整行宽度。
    /// </summary>
    // ReSharper disable once UnusedType.Global
    public class TextAreaAdaptiveAttributeDrawer : OdinAttributeDrawer<TextAreaAdaptiveAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            string text = Property.ValueEntry?.WeakSmartValue as string ?? string.Empty;
            GUIStyle style = EditorStyles.textArea;

            int minLines = Mathf.Max(1, Attribute.MinLines);
            int maxLines = Mathf.Max(minLines, Attribute.MaxLines);
            float chrome = style.padding.vertical;

            EditorGUILayout.TextArea(
                text,
                style,
                GUILayout.MinHeight(minLines * style.lineHeight + chrome),
                GUILayout.MaxHeight(maxLines * style.lineHeight + chrome),
                GUILayout.ExpandWidth(true));
        }
    }
}
