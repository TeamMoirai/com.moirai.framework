using System;
using System.Collections.Generic;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 对象池模块。
    /// </summary>
    internal sealed partial class ObjectPoolModule : Module, IObjectPoolModule, IUpdateModule
    {
        private const int DEFAULT_CAPACITY = int.MaxValue;
        private const float DEFAULT_EXPIRE_TIME = float.MaxValue;
        private const int DEFAULT_PRIORITY = 0;

        private Dictionary<TypeNamePair, ObjectPoolBase> _objectPools;
        private List<ObjectPoolBase> _cachedAllObjectPools;
        private Comparison<ObjectPoolBase> _objectPoolComparer;

        public override int Priority => 6;
        
        public int Count => _objectPools.Count;
        
        public override void OnInit()
        {
            _objectPools = new Dictionary<TypeNamePair, ObjectPoolBase>();
            _cachedAllObjectPools = new List<ObjectPoolBase>();
            _objectPoolComparer = ObjectPoolComparer;
            Log.Info("Object pool system onInit.");
        }
        
        public override void Shutdown()
        {
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                objectPool.Value.Shutdown();
            }

            _objectPools.Clear();
            _cachedAllObjectPools.Clear();
        }
        
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                objectPool.Value.Update(elapseSeconds, realElapseSeconds);
            }
        }

        public bool HasObjectPool<T>() where T : ObjectBase
        {
            return InternalHasObjectPool(new TypeNamePair(typeof(T)));
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool(Type objectType)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameException(StringUtility.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalHasObjectPool(new TypeNamePair(objectType));
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="name">对象池名称。</param>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalHasObjectPool(new TypeNamePair(typeof(T), name));
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="name">对象池名称。</param>
        /// <returns>是否存在对象池。</returns>
        public bool HasObjectPool(Type objectType, string name)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameException(StringUtility.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalHasObjectPool(new TypeNamePair(objectType, name));
        }

        public bool HasObjectPool(Predicate<ObjectPoolBase> condition)
        {
            if (condition == null)
            {
                throw new GameException("Condition is invalid.");
            }

            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public IObjectPool<T> GetObjectPool<T>() where T : ObjectBase
        {
            return (IObjectPool<T>)InternalGetObjectPool(new TypeNamePair(typeof(T)));
        }

        public ObjectPoolBase GetObjectPool(Type objectType)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameException(StringUtility.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalGetObjectPool(new TypeNamePair(objectType));
        }

        public IObjectPool<T> GetObjectPool<T>(string name) where T : ObjectBase
        {
            return (IObjectPool<T>)InternalGetObjectPool(new TypeNamePair(typeof(T), name));
        }

        public ObjectPoolBase GetObjectPool(Type objectType, string name)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameException(StringUtility.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalGetObjectPool(new TypeNamePair(objectType, name));
        }

        public ObjectPoolBase GetObjectPool(Predicate<ObjectPoolBase> condition)
        {
            if (condition == null)
            {
                throw new GameException("Condition is invalid.");
            }

            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    return objectPool.Value;
                }
            }

            return null;
        }

        public ObjectPoolBase[] GetObjectPools(Predicate<ObjectPoolBase> condition)
        {
            if (condition == null)
            {
                throw new GameException("Condition is invalid.");
            }

            List<ObjectPoolBase> results = new List<ObjectPoolBase>();
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    results.Add(objectPool.Value);
                }
            }

            return results.ToArray();
        }

        public void GetObjectPools(Predicate<ObjectPoolBase> condition, List<ObjectPoolBase> results)
        {
            if (condition == null)
            {
                throw new GameException("Condition is invalid.");
            }

            if (results == null)
            {
                throw new GameException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                if (condition(objectPool.Value))
                {
                    results.Add(objectPool.Value);
                }
            }
        }

        public ObjectPoolBase[] GetAllObjectPools()
        {
            return GetAllObjectPools(false);
        }

        public void GetAllObjectPools(List<ObjectPoolBase> results)
        {
            GetAllObjectPools(false, results);
        }

        public ObjectPoolBase[] GetAllObjectPools(bool sort)
        {
            if (sort)
            {
                List<ObjectPoolBase> results = new List<ObjectPoolBase>();
                foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
                {
                    results.Add(objectPool.Value);
                }

                results.Sort(_objectPoolComparer);
                return results.ToArray();
            }
            else
            {
                int index = 0;
                ObjectPoolBase[] results = new ObjectPoolBase[_objectPools.Count];
                foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
                {
                    results[index++] = objectPool.Value;
                }

                return results;
            }
        }

        public void GetAllObjectPools(bool sort, List<ObjectPoolBase> results)
        {
            if (results == null)
            {
                throw new GameException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<TypeNamePair, ObjectPoolBase> objectPool in _objectPools)
            {
                results.Add(objectPool.Value);
            }

            if (sort)
            {
                results.Sort(_objectPoolComparer);
            }
        }

        public IObjectPool<T> CreateSingleSpawnObjectPool<T>() where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }

        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name)
        {
            return InternalCreateObjectPool(objectType, name, false, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity)
        {
            return InternalCreateObjectPool(objectType, name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, false, expireTime, capacity, expireTime, priority);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, false, expireTime, capacity, expireTime, priority);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, expireTime, capacity, expireTime, priority);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, expireTime, capacity, expireTime, priority);
        }
        
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
            where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, false, autoReleaseInterval, capacity, expireTime, priority);
        }
        
        public ObjectPoolBase CreateSingleSpawnObjectPool(Type objectType, string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, false, autoReleaseInterval, capacity, expireTime, priority);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>() where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name)
        {
            return InternalCreateObjectPool(objectType, name, true, DEFAULT_EXPIRE_TIME, DEFAULT_CAPACITY, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity)
        {
            return InternalCreateObjectPool(objectType, name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, DEFAULT_CAPACITY, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity, float expireTime) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity, float expireTime)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, capacity, expireTime, DEFAULT_PRIORITY);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, DEFAULT_EXPIRE_TIME, capacity, DEFAULT_EXPIRE_TIME, priority);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, DEFAULT_CAPACITY, expireTime, priority);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(string.Empty, true, expireTime, capacity, expireTime, priority);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, string.Empty, true, expireTime, capacity, expireTime, priority);
        }
        
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, int capacity, float expireTime, int priority) where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, expireTime, capacity, expireTime, priority);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, expireTime, capacity, expireTime, priority);
        }

        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
            where T : ObjectBase
        {
            return InternalCreateObjectPool<T>(name, true, autoReleaseInterval, capacity, expireTime, priority);
        }
        
        public ObjectPoolBase CreateMultiSpawnObjectPool(Type objectType, string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
        {
            return InternalCreateObjectPool(objectType, name, true, autoReleaseInterval, capacity, expireTime, priority);
        }
        
        public bool DestroyObjectPool<T>() where T : ObjectBase
        {
            return InternalDestroyObjectPool(new TypeNamePair(typeof(T)));
        }
        
        public bool DestroyObjectPool(Type objectType)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameException(StringUtility.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalDestroyObjectPool(new TypeNamePair(objectType));
        }
        
        public bool DestroyObjectPool<T>(string name) where T : ObjectBase
        {
            return InternalDestroyObjectPool(new TypeNamePair(typeof(T), name));
        }
        
        public bool DestroyObjectPool(Type objectType, string name)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameException(StringUtility.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            return InternalDestroyObjectPool(new TypeNamePair(objectType, name));
        }

        public bool DestroyObjectPool<T>(IObjectPool<T> objectPool) where T : ObjectBase
        {
            if (objectPool == null)
            {
                throw new GameException("Object pool is invalid.");
            }

            return InternalDestroyObjectPool(new TypeNamePair(typeof(T), objectPool.Name));
        }

        public bool DestroyObjectPool(ObjectPoolBase objectPool)
        {
            if (objectPool == null)
            {
                throw new GameException("Object pool is invalid.");
            }

            return InternalDestroyObjectPool(new TypeNamePair(objectPool.ObjectType, objectPool.Name));
        }
        
        public void Release()
        {
            Log.Info("Object pool release...");
            GetAllObjectPools(true, _cachedAllObjectPools);
            foreach (ObjectPoolBase objectPool in _cachedAllObjectPools)
            {
                objectPool.Release();
            }
        }

        public void ReleaseAllUnused()
        {
            Log.Info("Object pool release all unused...");
            GetAllObjectPools(true, _cachedAllObjectPools);
            foreach (ObjectPoolBase objectPool in _cachedAllObjectPools)
            {
                objectPool.ReleaseAllUnused();
            }
        }
        
        private bool InternalHasObjectPool(TypeNamePair typeNamePair)
        {
            return _objectPools.ContainsKey(typeNamePair);
        }

        private ObjectPoolBase InternalGetObjectPool(TypeNamePair typeNamePair)
        {
            ObjectPoolBase objectPool = null;
            if (_objectPools.TryGetValue(typeNamePair, out objectPool))
            {
                return objectPool;
            }

            return null;
        }

        private IObjectPool<T> InternalCreateObjectPool<T>(string name, bool allowMultiSpawn, float autoReleaseInterval, int capacity, float expireTime,
            int priority) where T : ObjectBase
        {
            TypeNamePair typeNamePair = new TypeNamePair(typeof(T), name);
            if (HasObjectPool<T>(name))
            {
                throw new GameException(StringUtility.Format("Already exist object pool '{0}'.", typeNamePair));
            }

            ObjectPool<T> objectPool = new ObjectPool<T>(name, allowMultiSpawn, autoReleaseInterval, capacity, expireTime, priority);
            _objectPools.Add(typeNamePair, objectPool);
            return objectPool;
        }

        private ObjectPoolBase InternalCreateObjectPool(Type objectType, string name, bool allowMultiSpawn, float autoReleaseInterval, int capacity,
            float expireTime, int priority)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            if (!typeof(ObjectBase).IsAssignableFrom(objectType))
            {
                throw new GameException(StringUtility.Format("Object type '{0}' is invalid.", objectType.FullName));
            }

            TypeNamePair typeNamePair = new TypeNamePair(objectType, name);
            if (HasObjectPool(objectType, name))
            {
                throw new GameException(StringUtility.Format("Already exist object pool '{0}'.", typeNamePair));
            }

            Type objectPoolType = typeof(ObjectPool<>).MakeGenericType(objectType);
            ObjectPoolBase objectPool =
                (ObjectPoolBase)Activator.CreateInstance(objectPoolType, name, allowMultiSpawn, autoReleaseInterval, capacity, expireTime, priority);
            _objectPools.Add(typeNamePair, objectPool);
            return objectPool;
        }

        private bool InternalDestroyObjectPool(TypeNamePair typeNamePair)
        {
            ObjectPoolBase objectPool = null;
            if (_objectPools.TryGetValue(typeNamePair, out objectPool))
            {
                objectPool.Shutdown();
                return _objectPools.Remove(typeNamePair);
            }

            return false;
        }

        private static int ObjectPoolComparer(ObjectPoolBase a, ObjectPoolBase b)
        {
            return a.Priority.CompareTo(b.Priority);
        }
    }
}