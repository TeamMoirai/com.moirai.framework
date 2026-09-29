using UnityEditor;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 项目资产变更（含 Luban 转表回写、表格字节重导）后丢弃本地化编辑器预览缓存。
    /// </summary>
    /// <remarks>
    /// 挂在 <see cref="EditorApplication.projectChanged"/> 上；预览本就懒重建，失效仅置空引用，无性能负担。
    /// </remarks>
    [InitializeOnLoad]
    internal static class LocalizationPreviewInvalidator
    {
        static LocalizationPreviewInvalidator()
        {
            EditorApplication.projectChanged += LocalizationService.InvalidateEditorPreview;
        }
    }
}
