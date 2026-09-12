using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 持久化实体恢复事件参数（<see cref="SaveService.EntityRestored"/>）。
    /// <para>随 P4 先行定义；生产点由动态实体持久化（P7）接线。</para>
    /// </summary>
    public readonly struct SaveEntityRestoredArgs
    {
        /// <summary>
        /// 实体稳定标识。
        /// </summary>
        public string EntityId { get; }

        /// <summary>
        /// 预制体注册键（<c>SavePrefabRegistry</c>）。
        /// </summary>
        public string PrefabKey { get; }

        /// <summary>
        /// 恢复出的实体实例。
        /// </summary>
        public GameObject Instance { get; }

        /// <summary>
        /// 创建实体恢复参数。
        /// </summary>
        /// <param name="entityId">实体稳定标识。</param>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="instance">恢复出的实体实例。</param>
        public SaveEntityRestoredArgs(string entityId, string prefabKey, GameObject instance)
        {
            EntityId = entityId;
            PrefabKey = prefabKey;
            Instance = instance;
        }
    }
}
