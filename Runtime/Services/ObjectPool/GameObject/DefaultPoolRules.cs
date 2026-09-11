using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 未注册地址 / 外部预制体池的默认规则工厂。
    /// <para>与旧 Core 池「自动建池」行为对齐：Burst 策略，soft 8 / hard 64。</para>
    /// </summary>
    internal static class DefaultPoolRules
    {
        #region 常量 [CONSTANTS]

        /// <summary>
        /// 默认软容量。
        /// </summary>
        public const int SOFT_CAPACITY = 8;

        /// <summary>
        /// 默认硬容量。
        /// </summary>
        public const int HARD_CAPACITY = 64;

        /// <summary>
        /// 默认空闲超时秒数。
        /// </summary>
        public const float IDLE_SECONDS = 15f;

        /// <summary>
        /// 外部预制体池默认分组。
        /// </summary>
        public const string PREFAB_GROUP = "Prefab";

        private const string PREFAB_LOCATION_PREFIX = "Prefab:";

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 生成未注册资源地址的默认规则（仍按 Location 源加载/卸载预制体）。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <returns>编译后的默认规则。</returns>
        public static PoolCompiledRule CreateLocationRule(string location)
        {
            return CreateRule("Default", PoolEntry.DEFAULT_GROUP, location, unloadPrefab: true);
        }

        /// <summary>
        /// 生成外部预制体池的默认规则（池不卸载外部预制体）。
        /// </summary>
        /// <param name="location">池键。</param>
        /// <param name="group">分组名称。</param>
        /// <returns>编译后的默认规则。</returns>
        public static PoolCompiledRule CreateExternalRule(string location, string group = null)
        {
            return CreateRule("Prefab", string.IsNullOrWhiteSpace(group) ? PREFAB_GROUP : group.Trim(), location, unloadPrefab: false);
        }

        /// <summary>
        /// 由外部预制体引用生成稳定的合成池键。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        /// <returns>合成池键。</returns>
        public static string GetPrefabPoolLocation(GameObject prefab)
        {
            if (prefab == null)
            {
                return string.Empty;
            }

            return string.Concat(PREFAB_LOCATION_PREFIX, prefab.name, ":", prefab.GetInstanceID().ToString());
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static PoolCompiledRule CreateRule(string entryName, string group, string location, bool unloadPrefab)
        {
            return new PoolCompiledRule(
                ruleIndex: -1,
                entryName: entryName,
                group: group,
                pattern: location,
                policy: EPoolPolicy.Burst,
                minIdle: 0,
                softCapacity: SOFT_CAPACITY,
                hardCapacity: HARD_CAPACITY,
                idleSeconds: IDLE_SECONDS,
                unloadPrefab: unloadPrefab,
                priority: 0,
                matcher: PoolGlobMatcher.Compile(location));
        }

        #endregion
    }
}
