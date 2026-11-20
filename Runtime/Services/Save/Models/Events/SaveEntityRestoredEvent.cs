using UnityEngine;

namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 持久化实体恢复事件。
    /// </summary>
    /// <remarks>生产点为动态实体持久化恢复管线（<c>SaveEntityPersistence</c>）。</remarks>
    public readonly struct SaveEntityRestoredEvent : IGameEvent
    {
        /// <summary>实体稳定标识。</summary>
        public string EntityId { get; }

        /// <summary>预制体注册键（<c>SavePrefabRegistry</c>）。</summary>
        public string PrefabKey { get; }

        /// <summary>恢复出的实体实例。</summary>
        public GameObject Instance { get; }

        /// <summary>
        /// 创建实体恢复事件。
        /// </summary>
        /// <param name="entityId">实体稳定标识。</param>
        /// <param name="prefabKey">预制体注册键。</param>
        /// <param name="instance">恢复出的实体实例。</param>
        public SaveEntityRestoredEvent(string entityId, string prefabKey, GameObject instance)
        {
            EntityId = entityId;
            PrefabKey = prefabKey;
            Instance = instance;
        }
    }
}
