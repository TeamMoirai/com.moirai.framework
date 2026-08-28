using System;
using System.Collections.Generic;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 编译后的池规则。
    /// </summary>
    internal readonly struct PoolCompiledRule
    {
        #region 字段 [FIELDS]

        /// <summary>
        /// 规则索引。
        /// </summary>
        public readonly int RuleIndex;

        /// <summary>
        /// 条目名称。
        /// </summary>
        public readonly string EntryName;

        /// <summary>
        /// 分组名称。
        /// </summary>
        public readonly string Group;

        /// <summary>
        /// 匹配模式。
        /// </summary>
        public readonly string Pattern;

        /// <summary>
        /// 池策略。
        /// </summary>
        public readonly EPoolPolicy Policy;

        /// <summary>
        /// 最小空闲数量。
        /// </summary>
        public readonly int MinIdle;

        /// <summary>
        /// 软容量。
        /// </summary>
        public readonly int SoftCapacity;

        /// <summary>
        /// 硬容量。
        /// </summary>
        public readonly int HardCapacity;

        /// <summary>
        /// 空闲超时秒数。
        /// </summary>
        public readonly float IdleSeconds;

        /// <summary>
        /// 是否卸载预制体。
        /// </summary>
        public readonly bool UnloadPrefab;

        /// <summary>
        /// 优先级。
        /// </summary>
        public readonly int Priority;

        /// <summary>
        /// 匹配器。
        /// </summary>
        public readonly PoolGlobMatcher Matcher;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取是否为字面量模式。
        /// </summary>
        public bool IsLiteralPattern => Matcher.IsValid && Matcher.IsLiteralPattern;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="PoolCompiledRule"/> 的新实例。
        /// </summary>
        public PoolCompiledRule(
            int ruleIndex,
            string entryName,
            string group,
            string pattern,
            EPoolPolicy policy,
            int minIdle,
            int softCapacity,
            int hardCapacity,
            float idleSeconds,
            bool unloadPrefab,
            int priority,
            PoolGlobMatcher matcher)
        {
            RuleIndex = ruleIndex;
            EntryName = entryName;
            Group = group;
            Pattern = pattern;
            Policy = policy;
            MinIdle = minIdle;
            SoftCapacity = softCapacity;
            HardCapacity = hardCapacity;
            IdleSeconds = idleSeconds;
            UnloadPrefab = unloadPrefab;
            Priority = priority;
            Matcher = matcher;
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 从条目创建编译规则。
        /// </summary>
        /// <param name="entry">池条目。</param>
        /// <param name="ruleIndex">规则索引。</param>
        /// <returns>编译后的规则。</returns>
        public static PoolCompiledRule FromEntry(PoolEntry entry, int ruleIndex)
        {
            return new PoolCompiledRule(
                ruleIndex,
                entry.entryName,
                entry.group,
                entry.assetPath,
                entry.policy,
                entry.minIdle,
                entry.softCapacity,
                entry.hardCapacity,
                entry.idleSeconds,
                entry.unloadPrefab,
                entry.priority,
                PoolGlobMatcher.Compile(entry.assetPath));
        }

        #endregion
    }

    /// <summary>
    /// 编译后的池目录，管理精确匹配和通配符匹配。
    /// </summary>
    internal sealed class PoolCompiledCatalog
    {
        #region 字段 [FIELDS]

        private readonly PoolCompiledRule[] _rules;
        private readonly int[] _globRuleIndices;
        private readonly int _globRuleCount;
        private StringOpenHashMap _exactRuleMap;

        #endregion

        #region 构造 [CONSTRUCTOR]

        private PoolCompiledCatalog(PoolCompiledRule[] rules, int[] globRuleIndices, int globRuleCount, StringOpenHashMap exactRuleMap)
        {
            _rules = rules;
            _globRuleIndices = globRuleIndices;
            _globRuleCount = globRuleCount;
            _exactRuleMap = exactRuleMap;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取规则数量。
        /// </summary>
        public int RuleCount => _rules.Length;

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 获取指定索引的规则。
        /// </summary>
        /// <param name="ruleIndex">规则索引。</param>
        /// <returns>规则的只读引用。</returns>
        public ref readonly PoolCompiledRule GetRule(int ruleIndex)
        {
            return ref _rules[ruleIndex];
        }

        /// <summary>
        /// 解析资源地址对应的规则索引。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>规则索引，未找到返回 -1。</returns>
        public int Resolve(string location)
        {
            if (string.IsNullOrEmpty(location) || _rules.Length == 0)
            {
                return -1;
            }

            if (_exactRuleMap.TryGetValue(location, out int exactIndex))
            {
                return exactIndex;
            }

            for (int i = 0; i < _globRuleCount; i++)
            {
                int ruleIndex = _globRuleIndices[i];
                if (_rules[ruleIndex].Matcher.IsMatch(location))
                {
                    _exactRuleMap.AddOrUpdate(location, ruleIndex);
                    return ruleIndex;
                }
            }

            return -1;
        }

        /// <summary>
        /// 释放内部资源。
        /// </summary>
        public void Dispose()
        {
            _exactRuleMap.Dispose();
        }

        #endregion

        #region 静态方法 [STATIC METHODS]

        /// <summary>
        /// 创建空目录。
        /// </summary>
        /// <returns>空目录。</returns>
        public static PoolCompiledCatalog Empty()
        {
            return new PoolCompiledCatalog(
                Array.Empty<PoolCompiledRule>(),
                Array.Empty<int>(),
                0,
                new StringOpenHashMap(8));
        }

        /// <summary>
        /// 从条目列表构建目录。
        /// </summary>
        /// <param name="entries">条目列表。</param>
        /// <returns>编译后的目录。</returns>
        public static PoolCompiledCatalog Build(IList<PoolEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return Empty();
            }

            int validCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PoolEntry entry = entries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.assetPath))
                {
                    validCount++;
                }
            }

            if (validCount == 0)
            {
                return Empty();
            }

            PoolEntry[] normalized = new PoolEntry[validCount];
            int write = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PoolEntry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.assetPath))
                {
                    continue;
                }

                normalized[write++] = entry;
            }

            Array.Sort(normalized, PoolEntry.CompareByPriority);

            PoolCompiledRule[] rules = new PoolCompiledRule[normalized.Length];
            int[] globIndices = new int[normalized.Length];
            StringOpenHashMap exactMap = new StringOpenHashMap(normalized.Length);
            int globCount = 0;
            for (int i = 0; i < normalized.Length; i++)
            {
                PoolCompiledRule rule = PoolCompiledRule.FromEntry(normalized[i], i);
                rules[i] = rule;
                if (rule.IsLiteralPattern && !exactMap.ContainsKey(rule.Pattern))
                {
                    exactMap.AddOrUpdate(rule.Pattern, i);
                }
                else
                {
                    globIndices[globCount++] = i;
                }
            }

            return new PoolCompiledCatalog(rules, globIndices, globCount, exactMap);
        }

        #endregion
    }
}
