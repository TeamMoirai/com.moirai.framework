using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace Moirai.Atropos.ReferenceFinder
{
    /// <summary>
    /// 支持点击列头排序的多列表头，点击后按对应方式排序并刷新资源树视图。
    /// </summary>
    internal sealed class ClickColumn : MultiColumnHeader
    {
        /// <summary>
        /// 列排序回调。
        /// </summary>
        public delegate void SortInColumn();

        /// <summary>
        /// 列索引到排序回调的映射。
        /// </summary>
        public static Dictionary<int, SortInColumn> SortWithIndex = new Dictionary<int, SortInColumn>
        {
            { 0, SortByName },
            { 1, SortByPath }
        };

        /// <summary>
        /// 构造列表头并启用点击排序。
        /// </summary>
        /// <param name="state">列表头状态。</param>
        public ClickColumn(MultiColumnHeaderState state) : base(state) => canSort = true;

        /// <summary>
        /// 点击列头时触发对应列的排序，并刷新资源树视图的排序与展开状态。
        /// </summary>
        /// <param name="column">被点击的列。</param>
        /// <param name="columnIndex">被点击列的索引。</param>
        protected override void ColumnHeaderClicked(MultiColumnHeaderState.Column column, int columnIndex)
        {
            base.ColumnHeaderClicked(column, columnIndex);
            if (SortWithIndex.ContainsKey(columnIndex))
            {
                SortWithIndex[columnIndex].Invoke();
                ResourceReferenceInfo curWindow = EditorWindow.GetWindow<ResourceReferenceInfo>();
                curWindow.assetTreeView.SortExpandItem();
            }
        }

        /// <summary>
        /// 按资源名称排序。
        /// </summary>
        public static void SortByName() => SortHelper.SortByName();

        /// <summary>
        /// 按资源路径排序。
        /// </summary>
        public static void SortByPath() => SortHelper.SortByPath();
    }
}