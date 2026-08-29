using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// GameObject 池回收计划。
    /// </summary>
    internal readonly struct PoolRecyclePlan
    {
        #region 字段 [FIELDS]

        /// <summary>
        /// 保留目标数量。
        /// </summary>
        public readonly int RetainTarget;

        /// <summary>
        /// 裁剪预算。
        /// </summary>
        public readonly int TrimBudget;

        /// <summary>
        /// 是否强制裁剪。
        /// </summary>
        public readonly bool ForceTrim;

        /// <summary>
        /// 是否卸载预制体。
        /// </summary>
        public readonly bool UnloadPrefab;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化 <see cref="PoolRecyclePlan"/> 的新实例。
        /// </summary>
        /// <param name="retainTarget">保留目标数量。</param>
        /// <param name="trimBudget">裁剪预算。</param>
        /// <param name="forceTrim">是否强制裁剪。</param>
        /// <param name="unloadPrefab">是否卸载预制体。</param>
        public PoolRecyclePlan(int retainTarget, int trimBudget, bool forceTrim, bool unloadPrefab)
        {
            RetainTarget = retainTarget;
            TrimBudget = trimBudget;
            ForceTrim = forceTrim;
            UnloadPrefab = unloadPrefab;
        }

        #endregion
    }

    /// <summary>
    /// GameObject 池策略规划器——按策略与内存状态计算回收计划（纯函数，可 EditMode 直测）。
    /// </summary>
    internal static class PoolPolicyPlanner
    {
        #region 常量 [CONSTANTS]

        private const int TRIM_BUDGET_CAP = 16;

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 根据规则和当前状态计算回收计划。
        /// </summary>
        /// <param name="rule">编译后的池规则。</param>
        /// <param name="totalCount">当前对象总数。</param>
        /// <param name="lowMemory">是否处于低内存状态。</param>
        /// <returns>回收计划。</returns>
        public static PoolRecyclePlan Plan(in PoolCompiledRule rule, int totalCount, bool lowMemory)
        {
            int retain = rule.MinIdle;
            if (!lowMemory)
            {
                switch (rule.Policy)
                {
                    case EPoolPolicy.Fixed:
                        retain = Mathf.Clamp(rule.MinIdle, 0, rule.SoftCapacity);
                        break;
                    case EPoolPolicy.Burst:
                        retain = Mathf.Clamp(rule.MinIdle, 0, rule.SoftCapacity);
                        break;
                    case EPoolPolicy.Sticky:
                        retain = Mathf.Max(rule.MinIdle, totalCount);
                        break;
                }
            }

            int budget = Mathf.Clamp(rule.SoftCapacity >> 2, 1, TRIM_BUDGET_CAP);
            if (lowMemory)
            {
                budget = TRIM_BUDGET_CAP;
            }

            bool unloadPrefab = rule.UnloadPrefab && (lowMemory || rule.Policy != EPoolPolicy.Sticky);
            return new PoolRecyclePlan(retain, budget, lowMemory, unloadPrefab);
        }

        #endregion
    }
}
