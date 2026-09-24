using UnityEditor;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 项目资产变更（含 Luban 转表回写、表格字节重导）后丢弃本地化编辑器预览缓存。
    /// <para>预览存储本来就是懒重建（下次 Inspector 绘制时再取数），失效动作只是置空引用，
    /// 因此用 <see cref="EditorApplication.projectChanged"/> 这把宽口径钩子没有性能负担，
    /// 换来的是「转表后预览必然是新数据」这条不变量。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class LocalizationPreviewInvalidator
    {
        static LocalizationPreviewInvalidator()
        {
            EditorApplication.projectChanged += LocalizationService.InvalidateEditorPreview;
        }
    }
}
