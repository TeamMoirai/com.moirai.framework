using Moirai.Atropos.Localization;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 本地化组件的 Inspector 预览：在 ID 字段下方显示按预览语言解析出的译文、资源地址与该地址指向的资产。
    /// </summary>
    /// <remarks>
    /// 数据来源分两条：播放态走已注册服务；非播放态走编辑器预览入口（<c>ConfigTableService.GetAllLocalizedStringsForEditor</c>、
    /// <c>ResourceService.LoadAssetForEditor</c>），两条都不要求服务世界启动，无需进 Play。
    /// 预览取不到数据时只标注一行原因，不打断 Inspector 绘制，也不回写目标组件。
    /// 基类须为 <c>OdinEditor</c>，不可改用 <c>UnityEditor.Editor</c>（会顶掉 Odin 特性驱动的绘制）。
    /// </remarks>
    [CustomEditor(typeof(LocalizerBase), true)]
    internal sealed class LocalizerPreviewEditor : OdinEditor
    {
        private static readonly GUIContent s_PreviewContent = new GUIContent("译文预览 [Preview]");

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var localizer = target as LocalizerBase;
            if (localizer == null) return;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(s_PreviewContent, Describe(localizer));
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
            if (Application.isPlaying)
                return LocalizationService.IsValid ? "(未配置文本 ID)" : "(本地化服务未就绪)";

            return LocalizationService.IsEditorPreviewAvailable ? "(未配置文本 ID)" : "(表未生成或编辑器语言不在表内)";
        }
    }
}
