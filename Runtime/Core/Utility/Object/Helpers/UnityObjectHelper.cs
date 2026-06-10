using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 单机默认的对象管理器
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class UnityObjectHelper : ObjectUtility.IObjectHelper
    {
        public T InstantiateObject<T>(T original, bool playerOwned = false, bool allowNetworked = true) where T : Object
        {
            return Object.Instantiate(original);
        }

        public T InstantiateObject<T>(T original, Transform parent, bool playerOwned = false, bool allowNetworked = true) where T : Object
        {
            return Object.Instantiate(original, parent);
        }

        public T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, bool playerOwned = false, bool allowNetworked = true) where T : Object
        {
            return Object.Instantiate(original, position, rotation);
        }

        public T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, Transform parent, bool playerOwned = false, bool allowNetworked = true) where T : Object
        {
            return Object.Instantiate(original, position, rotation, parent);
        }

        public void DestroyObject(Object target, bool allowNetworked = true)
        {
            Object.Destroy(target);
        }
    }
}