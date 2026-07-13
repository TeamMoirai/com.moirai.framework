using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 提供对象创建/删除管理的基类。
    /// </summary>
    [Serializable]
    public abstract class ObjectHandler
    {
        internal void Internal_Init()
        {
            OnInit();
        }

        internal void Internal_Shutdown()
        {
            Shutdown();
        }

        protected abstract void OnInit();

        protected abstract void Shutdown();

        /// <summary>
        /// 实例化对象
        /// </summary>
        /// <param name="original">要实例化的原始对象</param>
        /// <param name="playerOwned">将对象注册为属于玩家</param>
        /// <param name="allowNetworked">注册为联网对象</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public abstract T InstantiateObject<T>(T original, bool playerOwned = false, bool allowNetworked = true)
            where T : UnityEngine.Object;

        /// <summary>
        /// 实例化对象
        /// </summary>
        /// <param name="original">要实例化的原始对象</param>
        /// <param name="parent">实例化对象的父级转换</param>
        /// <param name="playerOwned">将对象注册为属于玩家</param>
        /// <param name="allowNetworked">注册为联网对象</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public abstract T InstantiateObject<T>(T original, Transform parent, bool playerOwned = false,
            bool allowNetworked = true) where T : UnityEngine.Object;

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
        public abstract T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, bool playerOwned = false,
            bool allowNetworked = true) where T : UnityEngine.Object;

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
        public abstract T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, Transform parent,
            bool playerOwned = false, bool allowNetworked = true) where T : UnityEngine.Object;

        /// <summary>
        /// 销毁对象
        /// </summary>
        /// <param name="target">要销毁的对象</param>
        /// <param name="allowNetworked">销毁联网对象</param>
        public abstract void DestroyObject(UnityEngine.Object target, bool allowNetworked = true);
    }
}