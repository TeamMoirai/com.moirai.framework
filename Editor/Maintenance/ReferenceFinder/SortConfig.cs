using System.Collections.Generic;

namespace Moirai.Atropos.ReferenceFinder
{
    /// <summary>
    /// 排序方式切换配置，定义名称/路径排序的循环切换映射与分组号。
    /// </summary>
    internal sealed class SortConfig
    {
        /// <summary>
        /// 名称排序的循环切换映射：无 → 名称升序 → 名称降序 → 名称升序。
        /// </summary>
        public static readonly Dictionary<SortType, SortType> SortTypeChangeByNameHandler = new Dictionary<SortType, SortType>
        {
            { SortType.None, SortType.AscByName },
            { SortType.AscByName, SortType.DescByName },
            { SortType.DescByName, SortType.AscByName }
        };

        /// <summary>
        /// 路径排序的循环切换映射：无 → 路径升序 → 路径降序 → 路径升序。
        /// </summary>
        public static readonly Dictionary<SortType, SortType> SortTypeChangeByPathHandler = new Dictionary<SortType, SortType>
        {
            { SortType.None, SortType.AscByPath },
            { SortType.AscByPath, SortType.DescByPath },
            { SortType.DescByPath, SortType.AscByPath }
        };

        /// <summary>
        /// 排序方式到分组号的映射（1=路径组，2=名称组）。
        /// </summary>
        public static readonly Dictionary<SortType, short> SortTypeGroup = new Dictionary<SortType, short>
        {
            { SortType.None, 0 },
            { SortType.AscByPath, 1 },
            { SortType.DescByPath, 1 },
            { SortType.AscByName, 2 },
            { SortType.DescByName, 2 }
        };

        /// <summary>
        /// 名称排序分组号。
        /// </summary>
        public const short TYPE_BY_NAME_GROUP = 2;

        /// <summary>
        /// 路径排序分组号。
        /// </summary>
        public const short TYPE_BY_PATH_GROUP = 1;
    }
}