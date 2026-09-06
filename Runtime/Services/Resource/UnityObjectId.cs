using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// Unity 对象稳定 ID 工具，兼容 Unity 6000.5+ EntityId API。
    /// </summary>
    public static class UnityObjectId
    {
        /// <summary>
        /// 获取 Unity 对象的稳定 ID。
        /// </summary>
        /// <param name="target">目标对象。</param>
        /// <returns>稳定 ID，对象为空时返回 0。</returns>
        public static ulong Get(Object target)
        {
            if (target == null)
            {
                return 0;
            }

#if UNITY_6000_5_OR_NEWER
            return EntityId.ToULong(target.GetEntityId());
#else
            return unchecked((ulong)(uint)target.GetInstanceID());
#endif
        }
    }
}
