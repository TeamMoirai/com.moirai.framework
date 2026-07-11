using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 单机默认的对象管理器
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    [Serializable]
    public sealed class UnityObjectHandler : ObjectHandler
    {
        public override T InstantiateObject<T>(T original, bool playerOwned = false, bool allowNetworked = true)
        {
            return Object.Instantiate(original);
        }

        public override T InstantiateObject<T>(T original, Transform parent, bool playerOwned = false, bool allowNetworked = true)
        {
            return Object.Instantiate(original, parent);
        }

        public override T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, bool playerOwned = false, bool allowNetworked = true)
        {
            return Object.Instantiate(original, position, rotation);
        }

        public override T InstantiateObject<T>(T original, Vector3 position, Quaternion rotation, Transform parent, bool playerOwned = false, bool allowNetworked = true)
        {
            return Object.Instantiate(original, position, rotation, parent);
        }

        public override void DestroyObject(Object target, bool allowNetworked = true)
        {
            Object.Destroy(target);
        }
    }
}