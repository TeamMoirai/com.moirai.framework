namespace Moirai.Atropos.ReferenceFinder
{
    /// <summary>
    /// 资源引用列表的排序方式。
    /// </summary>
    public enum SortType
    {
        /// <summary>
        /// 不排序。
        /// </summary>
        None,
        /// <summary>
        /// 按名称升序。
        /// </summary>
        AscByName,
        /// <summary>
        /// 按名称降序。
        /// </summary>
        DescByName,
        /// <summary>
        /// 按路径升序。
        /// </summary>
        AscByPath,
        /// <summary>
        /// 按路径降序。
        /// </summary>
        DescByPath
    }
}