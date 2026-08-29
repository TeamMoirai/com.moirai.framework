using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池内 GameObject 销毁工具——Play 模式走延迟销毁（安全不破坏引用），
    /// EditMode（测试/编辑器工具实例化的池）走立即销毁。
    /// </summary>
    internal static class PoolDestroyUtility
    {
        /// <summary>
        /// 销毁指定对象（空安全）。
        /// </summary>
        /// <param name="obj">目标对象。</param>
        public static void Destroy(Object obj)
        {
            if (obj == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
#endif
            Object.Destroy(obj);
        }
    }
}
