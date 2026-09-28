using System;
using System.Collections.Generic;

namespace Moirai.Atropos.ReferenceFinder
{
    /// <summary>
    /// 资源引用列表排序辅助类，维护全局排序状态并提供比较函数与排序缓存。
    /// </summary>
    internal sealed class SortHelper
    {
        /// <summary>
        /// 字符串比较委托。
        /// </summary>
        public delegate int SortCompare(string lString, string rString);

        /// <summary>
        /// 已执行过排序的资产 GUID 集合。
        /// </summary>
        public static readonly HashSet<string> SortedGuid = new HashSet<string>();

        /// <summary>
        /// 资产路径到上次排序方式的缓存。
        /// </summary>
        public static readonly Dictionary<string, SortType> SortedAsset = new Dictionary<string, SortType>();

        /// <summary>
        /// 获取或设置当前排序方式。
        /// </summary>
        public static SortType CurSortType = SortType.None;

        /// <summary>
        /// 路径组上次使用的排序方式。
        /// </summary>
        public static SortType PathType = SortType.None;

        /// <summary>
        /// 名称组上次使用的排序方式。
        /// </summary>
        public static SortType NameType = SortType.None;

        /// <summary>
        /// 排序方式到比较函数的映射。
        /// </summary>
        public static readonly Dictionary<SortType, SortCompare> CompareFunction = new Dictionary<SortType, SortCompare>
        {
            { SortType.AscByPath, CompareWithPath },
            { SortType.DescByPath, CompareWithPathDesc },
            { SortType.AscByName, CompareWithName },
            { SortType.DescByName, CompareWithNameDesc }
        };

        /// <summary>
        /// 清空排序缓存。
        /// </summary>
        public static void Init()
        {
            SortedGuid.Clear();
            SortedAsset.Clear();
        }

        /// <summary>
        /// 切换排序方式：同组内在升/降序间循环，跨组时恢复该组上次的排序方式。
        /// </summary>
        /// <param name="sortGroup">目标排序分组号。</param>
        /// <param name="handler">该组的排序方式切换映射。</param>
        /// <param name="recoverType">该组上次使用的排序方式，切换后被更新为当前排序方式。</param>
        public static void ChangeSortType(short sortGroup, Dictionary<SortType, SortType> handler, ref SortType recoverType)
        {
            if (SortConfig.SortTypeGroup[CurSortType] == sortGroup)
            {
                CurSortType = handler[CurSortType];
            }
            else
            {
                CurSortType = recoverType;
                if (CurSortType == SortType.None) CurSortType = handler[CurSortType];
            }

            recoverType = CurSortType;
        }

        /// <summary>
        /// 按名称排序。
        /// </summary>
        public static void SortByName() => ChangeSortType(SortConfig.TYPE_BY_NAME_GROUP, SortConfig.SortTypeChangeByNameHandler, ref NameType);

        /// <summary>
        /// 按路径排序。
        /// </summary>
        public static void SortByPath() => ChangeSortType(SortConfig.TYPE_BY_PATH_GROUP, SortConfig.SortTypeChangeByPathHandler, ref PathType);

        /// <summary>
        /// 对指定资产的依赖与被引用列表排序（同组重复排序时用反转加速）。
        /// </summary>
        /// <param name="data">待排序的资产描述。</param>
        public static void SortChild(ReferenceFinderData.AssetDescription data)
        {
            if (data == null) return;
            if (SortedAsset.ContainsKey(data.path))
            {
                if (SortedAsset[data.path] == CurSortType) return;
                SortType oldSortType = SortedAsset[data.path];
                if (SortConfig.SortTypeGroup[oldSortType] == SortConfig.SortTypeGroup[CurSortType])
                {
                    FastSort(data.dependencies);
                    FastSort(data.references);
                }
                else
                {
                    NormalSort(data.dependencies);
                    NormalSort(data.references);
                }

                SortedAsset[data.path] = CurSortType;
            }
            else
            {
                NormalSort(data.dependencies);
                NormalSort(data.references);
                SortedAsset.Add(data.path, CurSortType);
            }
        }

        /// <summary>
        /// 使用当前排序方式的比较函数对字符串列表排序。
        /// </summary>
        /// <param name="strList">待排序的 GUID 列表。</param>
        public static void NormalSort(List<string> strList)
        {
            SortCompare curCompare = CompareFunction[CurSortType];
            strList.Sort((l, r) => curCompare(l, r));
        }

        /// <summary>
        /// 反转列表顺序（同组重复排序时的快速路径）。
        /// </summary>
        /// <param name="strList">待反转的列表。</param>
        public static void FastSort(List<string> strList)
        {
            int i = 0;
            int j = strList.Count - 1;
            while (i < j)
            {
                (strList[i], strList[j]) = (strList[j], strList[i]);
                i++;
                j--;
            }
        }

        /// <summary>
        /// 按资源名称升序比较。
        /// </summary>
        /// <param name="lString">左侧资源 GUID。</param>
        /// <param name="rString">右侧资源 GUID。</param>
        /// <returns>名称比较结果。</returns>
        public static int CompareWithName(string lString, string rString)
        {
            Dictionary<string, ReferenceFinderData.AssetDescription> asset = ResourceReferenceInfo.s_Data.assetDict;
            return string.Compare(asset[lString].name, asset[rString].name, StringComparison.Ordinal);
        }

        /// <summary>
        /// 按资源名称降序比较。
        /// </summary>
        /// <param name="lString">左侧资源 GUID。</param>
        /// <param name="rString">右侧资源 GUID。</param>
        /// <returns>名称比较结果的相反数。</returns>
        public static int CompareWithNameDesc(string lString, string rString) => 0 - CompareWithName(lString, rString);

        /// <summary>
        /// 按资源路径升序比较。
        /// </summary>
        /// <param name="lString">左侧资源 GUID。</param>
        /// <param name="rString">右侧资源 GUID。</param>
        /// <returns>路径比较结果。</returns>
        public static int CompareWithPath(string lString, string rString)
        {
            Dictionary<string, ReferenceFinderData.AssetDescription> asset = ResourceReferenceInfo.s_Data.assetDict;
            return string.Compare(asset[lString].path, asset[rString].path, StringComparison.Ordinal);
        }

        /// <summary>
        /// 按资源路径降序比较。
        /// </summary>
        /// <param name="lString">左侧资源 GUID。</param>
        /// <param name="rString">右侧资源 GUID。</param>
        /// <returns>路径比较结果的相反数。</returns>
        public static int CompareWithPathDesc(string lString, string rString) => 0 - CompareWithPath(lString, rString);
    }
}