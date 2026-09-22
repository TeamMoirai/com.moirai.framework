using Moirai.Atropos.Localization;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 本地化组件的 Inspector 预览：在 ID 字段下方直接显示按编辑器语言解析出的译文 / 将要取用的资源。
    /// <para>数据来源是配置表在编辑器下的直读路径（<c>ConfigTableServiceHandler.GetLocalizedStringsForEditorPreview</c>），
    /// 不依赖资源系统、也不需要进 Play；预览取不到数据时只标注一行原因，不打断 Inspector 绘制。</para>
    /// <para>刻意不把译文写回目标组件：那会把场景标脏，并留下"忘了还原"的错文案进版本库。</para>
    /// </summary>
    [CustomEditor(typeof(LocalizerBase), true)]
    internal sealed class LocalizerPreviewEditor : UnityEditor.Editor
    {
        private static readonly GUIContent PreviewContent = new GUIContent("译文预览 [Preview]");

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var localizer = target as LocalizerBase;
            if (localizer == null) return;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(PreviewContent, Describe(localizer));
            }
        }

        private static string Describe(LocalizerBase localizer)
        {
            string descriptor;
            try
            {
                descriptor = localizer.GetPreviewDescriptor();
            }
            catch (System.Exception ex)
            {
                // 预览在 Inspector 的重绘路径上，任何一次抛出都会让组件面板打不开
                return $"预览失败：{ex.Message}";
            }

            if (!string.IsNullOrEmpty(descriptor)) return descriptor;
            return LocalizationService.IsEditorPreviewAvailable
                ? "(未配置文本 ID)"
                : "(表未生成或编辑器语言不在表内)";
        }
    }
}
