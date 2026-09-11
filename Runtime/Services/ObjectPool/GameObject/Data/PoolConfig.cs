using System;
using Moirai.Atropos.Attributes;
using Sirenix.OdinInspector;
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

        [Tooltip("调试名。列表和运行时 Inspector 都看这个。")]
        [LabelText("规则名称（调试用）")]
        public string entryName = DEFAULT_ENTRY_NAME;

        [Tooltip("空闲实例挂到 [Group] 节点下。空值回落到 DefaultGroup。")]
        [LabelText("分组名称")]
        public string group = DEFAULT_GROUP;

        [Tooltip("location 或 glob。* 单级，** 递归，? 单字符。不含通配符则精确匹配。")]
        [LabelText("资源地址匹配模式")]
        public string pattern = string.Empty;

        [Tooltip("")]
        [LabelText("池策略")]
        public EPoolPolicy policy = EPoolPolicy.Burst;

        [Tooltip("维护后至少保留的空闲实例数。")]
        [LabelText("常驻空闲")]
        [Min(0)]
        public int minIdle;

        [Tooltip("空闲修剪目标上限。Burst / Fixed 超了会剪。")]
        [LabelText("软容量")]
        [Min(1)]
        public int softCapacity = 8;

        [Tooltip("总实例硬顶（含在场）。到达后 Spawn 返回 null。")]
        [LabelText("硬容量")]
        [Min(1)]
        public int hardCapacity = 16;

        [Tooltip("仅 Burst：最老空闲超过该秒数才剪。")]
        [LabelText("空闲秒数")]
        [EnumCondition(nameof(policy), (int)EPoolPolicy.Burst)]
        [Min(0f)]
        public float idleSeconds = 15f;

        [Tooltip("池被剪空后是否 UnloadAsset Prefab。")]
        [LabelText("空池卸载Prefab")]
        public bool unloadPrefab = true;

        [Tooltip("由左侧拖拽顺序自动维护，越靠上越先匹配。")]
        [LabelText("优先级")]
        public int priority;

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 规范化配置条目。
        /// </summary>
        /// <remarks>
        /// pattern 经 <see cref="NormalizeLocation"/>：去首尾空白、`\` 转 `/`、去掉尾部分隔符和扩展名。
        /// <para>不剥离 <c>Assets/Bundles/</c> 等路径前缀——pattern 须与运行时 location（或合成键）同形。</para>
        /// </remarks>
        /// <example>
        /// Assets/Bundles/Effects/Explosion.prefab  -> Assets/Bundles/Effects/Explosion
        /// Explosion                                -> Explosion
        /// Effects/**                               -> 递归匹配 Effects 下所有 location
        /// Prefab:Bullet*                           -> 匹配外部预制体合成池键（必须通配，字面量含 instanceID）
        /// </example>
        public void Normalize()
        {
            entryName = string.IsNullOrWhiteSpace(entryName) ? DEFAULT_ENTRY_NAME : entryName.Trim();
            group = string.IsNullOrWhiteSpace(group) ? DEFAULT_GROUP : group.Trim();
            pattern = NormalizeLocation(pattern);
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

            int leftLength = left.pattern == null ? 0 : left.pattern.Length;
            int rightLength = right.pattern == null ? 0 : right.pattern.Length;
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
