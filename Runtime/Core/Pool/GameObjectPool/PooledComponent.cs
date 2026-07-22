using UnityEngine;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos.Pool
{
    public class PooledComponent<T, TComponent> : PooledGameObject
        where TComponent : Component
        where T : PooledComponent<T, TComponent>, new()
    {
        /// <summary>
        /// 将组件缓存为元数据以减少分配
        /// </summary>
        public class ComponentCache : IPooledMetadata
        {
            public TComponent component;
        }
        
        public TComponent Component => Cache.component;
        
        protected ComponentCache Cache { get; set; }
        
        private static readonly PoolKey s_ComponentKey;
        
        static PooledComponent()
        {
            s_ComponentKey = new PoolKey(typeof(T).FullName);
        }
        
        internal static readonly _ObjectPool<T> Pool = new _ObjectPool<T>(() => new T());
        
        public new static void SetMaxSize(int size)
        {
            Pool.MaxSize = size;
        }
        
        /// <summary>
        /// 获取或创建空的池化组件
        /// </summary>
        /// <param name="parent"></param>
        /// <returns></returns>
        public static T Get(Transform parent = null)
        {
            var pooledComponent = Pool.Get();
            pooledComponent.PoolKey = s_ComponentKey;
            pooledComponent.GameObject = GameObjectPoolManager.Get(s_ComponentKey, out var metadata, parent, createEmptyIfNotExist: true);
            pooledComponent.Cache = metadata as ComponentCache;
            pooledComponent.Init();
            return pooledComponent;
        }
        
        private const string PREFIX = "Prefab";
        
        private static IPooledMetadata s_Metadata;
        
        public static PoolKey GetPooledKey(GameObject prefab, string prefix = "")
        {
            // 附加实例 ID，因为预制体可能是相同的名称
            return new PoolKey(string.IsNullOrEmpty(prefix) ? PREFIX : prefix, prefab.name, UnityUtility.GetObjectEntityId(prefab));
        }
        
        /// <summary>
        /// 通过预制体实例化池组件，优化 <see cref="Object.Instantiate(Object, Transform)"/> 
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="parent"></param>
        /// <param name="prefix">key 前缀，用于增加编辑器中分组的可读性</param>
        /// <returns></returns>
        public static T Instantiate(GameObject prefab, Transform parent = null, string prefix = "")
        {
            var pooledComponent = Pool.Get();
            var key = GetPooledKey(prefab, prefix);
            pooledComponent.PoolKey = key;
            var @object = GameObjectPoolManager.Get(key, out s_Metadata, parent, createEmptyIfNotExist: false);
            if (!@object)
            {
                @object = UObject.Instantiate(prefab, parent);
            }
            else
            {
                // 重置 transform
                @object.transform.position = prefab.transform.position;
                @object.transform.rotation = prefab.transform.rotation;
                @object.transform.localScale = prefab.transform.localScale;
            }

            pooledComponent.Cache = s_Metadata as ComponentCache;
            pooledComponent.GameObject = @object;
            pooledComponent.Init();
            return pooledComponent;
        }
        
        /// <summary>
        /// 通过预制体实例化池化组件，优化 <see cref="Object.Instantiate(Object, Vector3, Quaternion, Transform)"/> 
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <param name="parent">附加到父级。如果 parent 存在，它将使用 prefab 的 scale 作为 local scale 而不是 lossy scale</param>
        /// <param name="useLocalPosition">是否使用本地位置而不是世界位置，默认为<c>false</c></param>
        /// <param name="prefix">key 前缀，用于增加编辑器中分组的可读性</param>
        /// <returns></returns>
        public static T Instantiate(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null, bool useLocalPosition = false, string prefix = "")
        {
            var pooledComponent = Instantiate(prefab, parent, prefix);
            if (useLocalPosition)
                pooledComponent.GameObject.transform.SetLocalPositionAndRotation(position, rotation);
            else
                pooledComponent.GameObject.transform.SetPositionAndRotation(position, rotation);
            return pooledComponent;
        }
        
        protected override void Init()
        {
            IsDisposed = false;
            InitDisposables();
            Transform = GameObject.transform;
            Cache ??= new ComponentCache();
            if (!Cache.component)
            {
                // 获取组件
                Cache.component = GameObject.GetOrAddComponent<TComponent>();
            }
        }
        
        public sealed override void Dispose()
        {
            if (IsDisposed) return;
            OnDispose();
            ReleaseDisposables();
            if (GameObjectPoolManager.HasInstance) GameObjectPoolManager.Release(GameObject, PoolKey, Cache);
            IsDisposed = true;
            Pool.Release((T)this);
        }
        
        protected virtual void OnDispose() { }
    }
}