#if UNITY_6000_2_OR_NEWER
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
#else
using UnityEditor.IMGUI.Controls;
#endif

namespace Moirai.Atropos.ReferenceFinder
{
    internal sealed class AssetViewItem : TreeViewItem
    {
        public ReferenceFinderData.AssetDescription data;
    }
}