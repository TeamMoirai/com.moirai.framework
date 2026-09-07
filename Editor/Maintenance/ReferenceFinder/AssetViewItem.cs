#if UNITY_6000_2_OR_NEWER
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
#else
using UnityEditor.IMGUI.Controls;
#endif

namespace Moirai.Atropos.ReferenceFinder
{
    /// <summary>
    /// 引用查找树视图项，携带对应资源的引用描述数据。
    /// </summary>
    internal sealed class AssetViewItem : TreeViewItem
    {
        /// <summary>
        /// 该项对应的资源描述数据（路径、依赖与被引用信息）。
        /// </summary>
        public ReferenceFinderData.AssetDescription data;
    }
}