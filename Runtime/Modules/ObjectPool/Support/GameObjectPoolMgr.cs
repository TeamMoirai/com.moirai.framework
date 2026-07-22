using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池化对象基类。
    /// </summary>
    public class PoolObject : ObjectBase
    {
        public GameObject TargetGameObject => (GameObject)Target;

        // 缓存对象的初始位置、旋转、缩放。
        private static Vector3 s_Position;
        private static Quaternion s_Rotation;
        private static Vector3 s_Scale;

        /// <summary>
        /// 创建 PoolObject 对象。
        /// </summary>
        /// <param name="name">对象名称。</param>
        /// <param name="target">对象持有实例。</param>
        /// <returns></returns>
        public static PoolObject Create(string name, GameObject target)
        {
            var poolableObject = MemoryPool.Acquire<PoolObject>();
            poolableObject.Initialize(name, target);

            s_Position = target.transform.position;
            s_Rotation = target.transform.rotation;
            s_Scale = target.transform.localScale;

            return poolableObject;
        }

        #region Implements ObjectBase

        protected internal override void OnSpawn()
        {
            TargetGameObject.transform.position = s_Position;
            TargetGameObject.transform.rotation = s_Rotation;
            TargetGameObject.transform.localScale = s_Scale;

            TargetGameObject.SetActive(true);
        }

        protected internal override void OnDespawn()
        {
            TargetGameObject.SetActive(false);
        }

        protected internal override void Release(bool isShutdown)
        {
            if (TargetGameObject == null)
            {
                return;
            }

            ObjectUtility.DestroyObject(TargetGameObject.gameObject);
        }

        #endregion

    }

    public class PoolObjectData
    {
        public readonly GameObject parentObj;
        public readonly IObjectPool<PoolObject> pool;

        public PoolObjectData(string poolName, Transform poolRoot)
        {
            this.parentObj = new GameObject(poolName);
            parentObj.transform.SetParent(poolRoot);
            pool = GameModule.ObjectPool.CreateSingleSpawnObjectPool<PoolObject>(poolName, 300f, 100, 60f, 0);
        }
    }

    /// <summary>
    /// 基于 GameModule.Object 的对象池管理器。
    /// </summary>
    public class GameObjectPoolMgr : SingletonMono_Persistent<GameObjectPoolMgr>
    {
        private readonly Dictionary<string, PoolObjectData> _poolDic = new Dictionary<string, PoolObjectData>();

        public PoolObject Spawn(GameObject target, Transform parent = null)
        {
            if (target == null)
            {
                Log.Error("target GameObject is null");
                return null;
            }

            string key = $"{target.name}_{UnityUtility.GetObjectEntityId(target)}";
            IObjectPool<PoolObject> pool = null;
            PoolObject ret = null;
            if (_poolDic.TryGetValue(key, out var poolData))
            {
                pool = poolData.pool;
            }
            else
            {
                _poolDic.Add(key, new PoolObjectData(key, this.gameObject.transform));
                pool = _poolDic.Last().Value.pool;
            }

            if (pool.CanSpawn(key))
            {
                ret = pool.Spawn(key);
            }
            else
            {
                // 创建一个新对象。
                ret = PoolObject.Create(key, ObjectUtility.InstantiateObject(target));
                pool.Register(ret, true);
            }

            ret.TargetGameObject.transform.SetParent(parent);
            if (parent == null)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ret.TargetGameObject, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            return ret;
        }

        public void Despawn(PoolObject target)
        {
            PoolObjectData poolObjectData = _poolDic[target.Name];
            target.TargetGameObject.transform.SetParent(poolObjectData.parentObj.transform);
            poolObjectData.pool.Despawn(target);
        }
    }
}