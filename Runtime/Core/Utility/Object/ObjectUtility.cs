using UnityEngine;

namespace Moirai.Atropos
{
    public static partial class ObjectUtility
    {
        private static ObjectHandler s_Handler = null;
        /// <summary>
        /// 获取/设置游戏对象处理器。
        /// </summary>
        public static ObjectHandler Handler
        {
            get
            {
                if (s_Handler == null) Handler = new UnityObjectHandler();
                return s_Handler;
            }
            set
            {
                if (s_Handler == value || value == null) return;

                s_Handler?.Internal_Shutdown();
                s_Handler = value;
                s_Handler.Internal_Init();
            }
        }

        /// <summary>
        /// 实例化对象
        /// </summary>
        /// <param name="original">要实例化的原始对象</param>
        /// <param name="playerOwned">将对象注册为属于玩家</param>
        /// <param name="allowNetworked">注册为联网对象</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T InstantiateObject<T>(T original, bool playerOwned = false, bool allowNetworked = true)
            where T : UnityEngine.Object
        {
            return Handler.InstantiateObject(original, playerOwned, allowNetworked);
        }
        
        /// <summary>
        /// 实例化对象
        /// </summary>
        /// <param name="original">要实例化的原始对象</param>
        /// <param name="parent">实例化对象的父级转换</param>
        /// <param name="playerOwned">将对象注册为属于玩家</param>
        /// <param name="allowNetworked">注册为联网对象</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T InstantiateObject<T>(T original, Transform parent, bool playerOwned = false,
            bool allowNetworked = true) where T : UnityEngine.Object
        {
            return Handler.InstantiateObject(original, parent, playerOwned, allowNetworked);
        }

        /// <summary>
        /// 实例化对象
        /// </summary>
        /// <param name="original">要实例化的原始对象</param>
        /// <param name="position">实例化对象的位置</param>
        /// <param name="rotation">实例化对象的旋转</param>
        /// <param name="playerOwned">将对象注册为属于玩家</param>
        /// <param name="allowNetworked">注册为联网对象</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation,
            bool playerOwned = false, bool allowNetworked = true) where T : UnityEngine.Object
        {
            return Handler.InstantiateObject(original, position, rotation, playerOwned, allowNetworked);
        }

        /// <summary>
        /// 实例化对象
        /// </summary>
        /// <param name="original">要实例化的原始对象</param>
        /// <param name="position">实例化对象的位置</param>
        /// <param name="rotation">实例化对象的旋转</param>
        /// <param name="parent">实例化对象的父级转换</param>
        /// <param name="playerOwned">将对象注册为属于玩家</param>
        /// <param name="allowNetworked">注册为联网对象</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, Transform parent,
            bool playerOwned = false, bool allowNetworked = true) where T : UnityEngine.Object
        {
            return Handler.InstantiateObject(original, position, rotation, parent, playerOwned, allowNetworked);
        }

        /// <summary>
        /// 销毁对象
        /// </summary>
        /// <param name="target">要销毁的对象</param>
        /// <param name="allowNetworked">销毁联网对象</param>
        public static void DestroyObject(UnityEngine.Object target, bool allowNetworked = true)
        {
            Handler.DestroyObject(target, allowNetworked);
        }
    }
}