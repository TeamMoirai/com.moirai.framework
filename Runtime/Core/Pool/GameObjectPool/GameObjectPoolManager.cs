using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Pool
{
    /// <summary>
    /// 池化对象元数据的接口
    /// </summary>
    public interface IPooledMetadata { }
    
    /// <summary>
    /// 无分配的基于结构的池键
    /// </summary>
    public readonly struct PoolKey
    {
        private readonly string _key;
        private readonly string _subKey;
        private readonly int _id;
        
        public PoolKey(string key)
        {
            _key = key;
            _subKey = null;
            _id = 0;
        }
        
        public PoolKey(string key, string subKey)
        {
            _key = key;
            _subKey = subKey;
            _id = 0;
        }
        
        public PoolKey(string key, int id)
        {
            _key = key;
            _subKey = null;
            _id = id;
        }
        
        public PoolKey(string key, string subKey, int id)
        {
            _key = key;
            _subKey = subKey;
            _id = id;
        }
        
        public bool IsNull()
        {
            if (string.IsNullOrEmpty(_key)) return true;
            bool isNull = true;
            isNull &= string.IsNullOrEmpty(_subKey);
            isNull &= _id == 0;
            return isNull;
        }
        
        public override string ToString()
        {
            if (IsNull()) return string.Empty;
            if (string.IsNullOrEmpty(_subKey))
            {
                if (_id != 0) return $"{_key}({_id})";
                return _key;
            }
            if (_id != 0) return $"{_key}-{_subKey}({_id})";
            return $"{_key}-{_subKey}";
        }
        
        public class Comparer : IEqualityComparer<PoolKey>
        {
            public bool Equals(PoolKey x, PoolKey y)
            {
                return x._id == y._id && x._key == y._key && x._subKey == y._subKey;
            }

            public int GetHashCode(PoolKey key)
            {
                return HashCode.Combine(key._id, key._key, key._subKey);
            }
        }
    }
    
    /// <summary>
    /// 零分配游戏对象/组件池。
    /// </summary>
    public sealed class GameObjectPoolManager : SingletonMono<GameObjectPoolManager>
    {
        private readonly Dictionary<PoolKey, GameObjectPool> _poolDic = new Dictionary<PoolKey, GameObjectPool>(new PoolKey.Comparer());

        protected override void OnInit()
        {
            s_Instance.hideFlags = HideFlags.DontSave;
        }

        protected override void Shutdown()
        {
            LocalReleaseAll();
        }
        
        /// <summary>
        /// 获取池化游戏对象
        /// </summary>
        /// <param name="key"></param>
        /// <param name="pooledMetadata"></param>
        /// <param name="parent"></param>
        /// <param name="createEmptyIfNotExist"></param>
        /// <returns></returns>
        public static GameObject Get(PoolKey key, out IPooledMetadata pooledMetadata, Transform parent = null, bool createEmptyIfNotExist = true)
        {
            GameObject obj = null;
            pooledMetadata = null;
            if (Instance._poolDic.TryGetValue(key, out GameObjectPool poolData) && poolData.PoolQueue.Count > 0)
            {
                obj = poolData.GetObj(parent, out pooledMetadata);
            }
            else if (createEmptyIfNotExist)
            {
                obj = new GameObject(key.ToString());
                obj.transform.SetParent(parent);
            }
            return obj;
        }
        
        /// <summary>
        /// 将游戏对象释放回池
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="key"></param>
        /// <param name="pooledMetadata"></param>
        public static void Release(GameObject obj, PoolKey key = default, IPooledMetadata pooledMetadata = null)
        {
            if (obj == null) return;

            if (key.IsNull())
                key = new PoolKey(obj.name);
            if (!Instance._poolDic.TryGetValue(key, out GameObjectPool poolData))
            {
                poolData = Instance._poolDic[key] = new GameObjectPool(key, Instance.transform);
            }
            poolData.PushObj(obj, pooledMetadata);
        }
        
        /// <summary>
        /// 释放指定地址的游戏对象回池
        /// </summary>
        /// <param name="key"></param>
        public static void ReleasePool(PoolKey key)
        {
            if (Instance._poolDic.TryGetValue(key, out var pool))
            {
                pool.Release();
                Instance._poolDic.Remove(key);
            }
        }
        
        private void LocalReleaseAll()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
            _poolDic.Clear();
        }
        
        /// <summary>
        /// 释放所有池化游戏对象
        /// </summary>
        public static void ReleaseAll()
        {
            Instance.LocalReleaseAll();
        }
        
        private class GameObjectPool
        {
            public readonly GameObject ParentObj;
            
            public readonly Queue<GameObject> PoolQueue = new Queue<GameObject>();
            
            private readonly Dictionary<GameObject, IPooledMetadata> _metaData = new Dictionary<GameObject, IPooledMetadata>();
            
            public GameObjectPool(PoolKey address, Transform poolRoot)
            {
                ParentObj = new GameObject(address.ToString());
                ParentObj.transform.SetParent(poolRoot);
            }
            
            public void PushObj(GameObject obj, IPooledMetadata pooledMetadata)
            {
                PoolQueue.Enqueue(obj);
                _metaData[obj] = pooledMetadata;
                obj.transform.SetParent(ParentObj.transform);
                obj.SetActive(false);
            }
            
            public GameObject GetObj(Transform parent, out IPooledMetadata pooledMetadata)
            {
                GameObject obj = PoolQueue.Dequeue();
                _metaData.Remove(obj, out pooledMetadata);
                obj.SetActive(true);
                obj.transform.SetParent(parent);
                if (parent == null)
                {
                    SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());
                }
                return obj;
            }

            public void Release()
            {
                while (PoolQueue.TryDequeue(out var instance))
                {
                    Destroy(instance);
                }
                PoolQueue.Clear();
            }
        }
    }
}