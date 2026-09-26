using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档数据块组合器：块列表的不可变合并/替换/删除/查找（纯函数，供 EditMode 测试直调）。
    /// <para>所有操作返回新数组（源列表不被修改），调用方决定写回时机——保证读-改-写中断时原文件不受影响。</para>
    /// </summary>
    internal static class SaveBlockComposer
    {
        /// <summary>
        /// 在块列表中按键查找数据块。
        /// </summary>
        /// <param name="source">源块列表（可为 null = 空列表）。</param>
        /// <param name="key">目标键。</param>
        /// <param name="entry">命中时的数据块条目。</param>
        /// <returns>命中返回 <c>true</c>。</returns>
        public static bool TryFind(List<SaveBlockEntry> source, string key, out SaveBlockEntry entry)
        {
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (string.Equals(source[i].Key, key, StringComparison.Ordinal))
                    {
                        entry = source[i];
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// 按键插入或替换数据块（同键覆盖，保持原插入位置；新键追加尾部）。
        /// </summary>
        /// <param name="source">源块列表（可为 null = 空列表）。</param>
        /// <param name="replacement">替换条目。</param>
        /// <returns>合并后的新块列表。</returns>
        public static List<SaveBlockEntry> Upsert(List<SaveBlockEntry> source, SaveBlockEntry replacement)
        {
            var merged = new List<SaveBlockEntry>((source?.Count ?? 0) + 1);
            bool replaced = false;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (string.Equals(source[i].Key, replacement.Key, StringComparison.Ordinal))
                    {
                        merged.Add(replacement);
                        replaced = true;
                    }
                    else
                    {
                        merged.Add(source[i]);
                    }
                }
            }

            if (!replaced)
            {
                merged.Add(replacement);
            }

            return merged;
        }

        /// <summary>
        /// 批量按键插入或替换数据块（单次 O(n+m) 归并；同键以 additions 中最后一次为准，新键按该次出现顺序追加）。
        /// </summary>
        /// <param name="source">源块列表（可为 null = 空列表）。</param>
        /// <param name="additions">待合并条目列表。</param>
        /// <returns>合并后的新块列表。</returns>
        public static List<SaveBlockEntry> UpsertAll(List<SaveBlockEntry> source, List<SaveBlockEntry> additions)
        {
            if (additions == null || additions.Count == 0)
            {
                return source != null ? new List<SaveBlockEntry>(source) : new List<SaveBlockEntry>();
            }

            // 每个键只保留 additions 中最后一次出现
            var lastIndexOfKey = new Dictionary<string, int>(additions.Count, StringComparer.Ordinal);
            for (int i = 0; i < additions.Count; i++)
            {
                lastIndexOfKey[additions[i].Key] = i;
            }

            int sourceCount = source?.Count ?? 0;
            var sourceKeys = new HashSet<string>(sourceCount, StringComparer.Ordinal);
            for (int i = 0; i < sourceCount; i++)
            {
                sourceKeys.Add(source[i].Key);
            }

            var result = new List<SaveBlockEntry>(sourceCount + lastIndexOfKey.Count);
            for (int i = 0; i < sourceCount; i++)
            {
                SaveBlockEntry existing = source[i];
                if (lastIndexOfKey.TryGetValue(existing.Key, out int replacementIndex))
                {
                    result.Add(additions[replacementIndex]);
                }
                else
                {
                    result.Add(existing);
                }
            }

            for (int i = 0; i < additions.Count; i++)
            {
                if (lastIndexOfKey[additions[i].Key] == i && !sourceKeys.Contains(additions[i].Key))
                {
                    result.Add(additions[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 按键移除数据块（未命中返回等价于源列表的新列表）。
        /// </summary>
        /// <param name="source">源块列表（可为 null = 空列表）。</param>
        /// <param name="key">目标键。</param>
        /// <returns>移除后的新块列表。</returns>
        public static List<SaveBlockEntry> Remove(List<SaveBlockEntry> source, string key)
        {
            var remaining = new List<SaveBlockEntry>(source?.Count ?? 0);
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (!string.Equals(source[i].Key, key, StringComparison.Ordinal))
                    {
                        remaining.Add(source[i]);
                    }
                }
            }

            return remaining;
        }
    }
}
