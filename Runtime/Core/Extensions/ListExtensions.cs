using System.Collections.Generic;

namespace Moirai.Atropos
{
    public static partial class ListExtensions
    {
        /// <summary>
        /// 生成仅包含已更改项的列表。
        /// </summary>
        /// <param name="current">包含当前数据的列表</param>
        /// <param name="original">未修改数据列表</param>
        /// <typeparam name="T"></typeparam>
        /// <returns>完全匹配项返回 null。</returns>
        public static List<T> GenerateListChanges<T>(this List<T> current, List<T> original) where T : IMatchComparable<T>
        {
            if (current.Matches(original)) return null;

            List<T> result = new List<T>();

            for (int i = 0; i < result.Count; i++)
            {
                if (original != null && i < original.Count && result[i].Matches(original[i]))
                {
                    result.Add(default);
                }
                else
                {
                    result.Add(current[i]);
                }
            }

            return result;
        }
        
        /// <summary>
        /// 生成仅包含已更改项的列表。
        /// </summary>
        /// <param name="current">包含当前数据的列表</param>
        /// <param name="original">未修改数据列表</param>
        /// <returns>完全匹配项返回 null。</returns>
        public static List<string> GenerateStringListChanges(this List<string> current, List<string> original)
        {
            if (current.Matches(original)) return null;

            List<string> result = new List<string>();

            for(int i = 0; i < result.Count; i++)
            {
                if(original != null && i < original.Count && result[i] == original[i])
                {
                    result.Add(null);
                }
                else
                {
                    result.Add(current[i]);
                }
            }

            return result;
        }
        
        /// <summary>
        /// 检查两个列表是否相同
        /// </summary>
        /// <param name="self">列表</param>
        /// <param name="other">要比较的列表</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static bool Matches<T>(this IList<T> self, IList<T> other) where T : IMatchComparable<T>
        {
            if (self != null)
            {
                if (self.Count != other?.Count) return false;
                for (int i = 0; i < self.Count; i++)
                {
                    if (!self[i].Matches(other[i])) return false;
                }
            }
            else if (other != null) return false;

            return true;
        }
        
        /// <summary>
        /// 将对象与另一个副本进行比较
        /// </summary>
        /// <param name="self">原始列表</param>
        /// <param name="other">进行比较的副本</param>
        /// <returns></returns>
        public static bool Matches(this List<string> self, List<string> other)
        {
            if (self != null)
            {
                if (other == null) return false;
                if (self.Count != other.Count) return false;
                for (int i = 0; i < self.Count; i++)
                {
                    if (self[i] != other[i]) return false;
                }
            }
            else if (other != null) return false;

            return true;
        }

        /// <summary>
        /// 从列表中随机返回一个元素
        /// </summary>
        /// <param name="list"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T Random<T>(this IList<T> list)
        {
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

    }
}