using System;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池配置条目。
    /// </summary>
    [Serializable]
    public sealed class PoolEntry
    {
        #region 常量 [CONSTANTS]

        /// <summary>
        /// 默认分组名称。
        /// </summary>
        public const string DEFAULT_GROUP = "DefaultGroup";

        /// <summary>
        /// 默认条目名称。
        /// </summary>
        public const string DEFAULT_ENTRY_NAME = "PoolRule";

        #endregion

        #region 序列化字段 [SERIALIZED FIELDS]

        /// <summary>
        /// 条目名称。
        /// </summary>
        public string entryName = DEFAULT_ENTRY_NAME;

        /// <summary>
        /// 分组名称。
        /// </summary>
        public string group = DEFAULT_GROUP;

        /// <summary>
        /// 资源地址。
        /// </summary>
        public string assetPath = string.Empty;

        /// <summary>
        /// 池策略。
        /// </summary>
        public EPoolPolicy policy = EPoolPolicy.Burst;

        /// <summary>
        /// 最小空闲数量。
        /// </summary>
        [Min(0)]
        public int minIdle;

        /// <summary>
        /// 软容量。
        /// </summary>
        [Min(1)]
        public int softCapacity = 8;

        /// <summary>
        /// 硬容量。
        /// </summary>
        [Min(1)]
        public int hardCapacity = 16;

        /// <summary>
        /// 空闲超时秒数。
        /// </summary>
        [Min(0f)]
        public float idleSeconds = 15f;

        /// <summary>
        /// 是否在空闲时卸载预制体。
        /// </summary>
        public bool unloadPrefab = true;

        /// <summary>
        /// 优先级。
        /// </summary>
        public int priority;

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 规范化配置条目。
        /// </summary>
        public void Normalize()
        {
            entryName = string.IsNullOrWhiteSpace(entryName) ? DEFAULT_ENTRY_NAME : entryName.Trim();
            group = string.IsNullOrWhiteSpace(group) ? DEFAULT_GROUP : group.Trim();
            assetPath = NormalizeLocation(assetPath);
            if (!Enum.IsDefined(typeof(EPoolPolicy), policy))
            {
                policy = EPoolPolicy.Burst;
            }

            minIdle = Mathf.Max(0, minIdle);
            softCapacity = Mathf.Max(1, softCapacity);
            hardCapacity = Mathf.Max(softCapacity, hardCapacity);
            if (minIdle > hardCapacity)
            {
                minIdle = hardCapacity;
            }

            idleSeconds = policy == EPoolPolicy.Burst ? Mathf.Max(0f, idleSeconds) : 0f;
        }

        /// <summary>
        /// 按优先级比较条目。
        /// </summary>
        public static int CompareByPriority(PoolEntry left, PoolEntry right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int priorityCompare = right.priority.CompareTo(left.priority);
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            int leftLength = left.assetPath == null ? 0 : left.assetPath.Length;
            int rightLength = right.assetPath == null ? 0 : right.assetPath.Length;
            int pathLengthCompare = rightLength.CompareTo(leftLength);
            if (pathLengthCompare != 0)
            {
                return pathLengthCompare;
            }

            return string.Compare(left.group, right.group, StringComparison.Ordinal);
        }

        /// <summary>
        /// 规范化资源地址。
        /// </summary>
        public static string NormalizeLocation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int start = 0;
            int end = value.Length - 1;
            while (start <= end && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            while (end >= start && char.IsWhiteSpace(value[end]))
            {
                end--;
            }

            while (end >= start && (value[end] == '/' || value[end] == '\\'))
            {
                end--;
            }

            if (end < start)
            {
                return string.Empty;
            }

            bool hasBackslash = false;
            for (int i = start; i <= end; i++)
            {
                if (value[i] == '\\')
                {
                    hasBackslash = true;
                    break;
                }
            }

            string normalized = start == 0 && end == value.Length - 1
                ? value
                : value.Substring(start, end - start + 1);
            if (hasBackslash)
            {
                normalized = normalized.Replace('\\', '/');
            }

            int lastSlash = normalized.LastIndexOf('/');
            int extension = normalized.LastIndexOf('.');
            if (extension > lastSlash)
            {
                normalized = normalized.Substring(0, extension);
            }

            return normalized;
        }

        #endregion
    }
}
