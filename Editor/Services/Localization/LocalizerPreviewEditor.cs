using Moirai.Atropos.Localization;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 本地化组件的 Inspector 预览：在 ID 字段下方直接显示按预览语言解析出的译文 / 资源地址 / 该地址指向的资产。
    /// <para>数据来源按状态分两条：<b>播放态</b>走已注册的服务（本地化语言、后端取到的资产都是真的那份）；
    /// <b>非播放态</b>走编辑器预览入口——表数据经 <c>ConfigTableService.GetAllLocalizedStringsForEditor</c>，
    /// 地址到资产经 <c>ResourceService.LoadAssetForEditor</c>，两者都不要求服务世界起来，不需要进 Play。</para>
    /// <para>预览取不到数据时只标注一行原因，不打断 Inspector 绘制；也刻意不把内容写回目标组件：
    /// 那会把场景标脏，并留下"忘了还原"的错文案进版本库。</para>
    /// <para>基类用 <c>OdinEditor</c>（与全局 <c>MonoBehaviourEditor</c> 同源）而非 <c>UnityEditor.Editor</c>：
    /// 后者的 <c>DrawDefaultInspector</c> 会把派生本地化器上 Odin 特性驱动的绘制整个顶掉。</para>
    /// </summary>
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
