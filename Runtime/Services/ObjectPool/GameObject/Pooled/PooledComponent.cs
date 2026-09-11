using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Pool;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池化组件租约：在 <see cref="PooledGameObject"/> 之上缓存目标组件，跨池复用保留（存于 Slot.UserData）。
    /// <para>通用场景请用 <see cref="Pooled{TComponent}"/>；需要自定义 Init / 组件解析时继承本类型（CRTP）。</para>
    /// </summary>
    /// <typeparam name="T">包装器自身类型。</typeparam>
    /// <typeparam name="TComponent">目标组件类型。</typeparam>
    public class PooledComponent<T, TComponent> : PooledGameObject
        where TComponent : Component
        where T : PooledComponent<T, TComponent>, new()
    {
        #region 常量 [CONSTANTS]

        internal static readonly Internal_ObjectPool<T> s_Pool = new Internal_ObjectPool<T>(() => new T());

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取缓存的目标组件。
        /// </summary>
        public TComponent Component => Cache.Component;

        private ComponentCache _cache;
        /// <summary>
        /// 获取或初始化组件缓存。
        /// </summary>
        protected ComponentCache Cache
        {
            get
            {
                if (_cache == null)
                {
                    _cache = GetOrAddUserData<ComponentCache>();
                }

                return _cache;
            }
            set => _cache = value;
        }
        
        #endregion

        #region 嵌套类型 [NESTED TYPES]

        /// <summary>
        /// 组件缓存，随 Slot.UserData 跨池复用保留。
        /// </summary>
        public class ComponentCache
        {
            /// <summary>
            /// 缓存的目标组件。
            /// </summary>
            public TComponent Component;
        }

        #endregion

        #region 公共方法 — Spawn [PUBLIC SPAWN]

        /// <summary>
        /// 将已生成实例包装为组件租约。
        /// </summary>
        /// <param name="instance">池化实例。</param>
        /// <returns>组件池化租约。</returns>
        public new static T Wrap(GameObject instance)
        {
            if (!GameObjectPoolService.TryResolveInstance(instance, out RuntimeGameObjectPool pool, out int slotIndex, out uint generation))
            {
                return null;
            }

            T pooled = s_Pool.Get();
            pooled.Bind(pool, slotIndex, generation);
            return pooled;
        }

        /// <summary>
        /// 按资源地址同步获取组件租约。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件池化租约。</returns>
        public new static T Spawn(string location, Transform parent = null) =>
            Wrap(GameObjectPoolService.Spawn(location, parent));

        /// <summary>
        /// 以外部预制体同步获取组件租约。回池复用时重置到预制体局部姿态。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件池化租约。</returns>
        public new static T Spawn(GameObject prefab, Transform parent = null)
        {
            GameObject instance = GameObjectPoolService.Spawn(prefab, parent);
            if (instance == null)
            {
                return null;
            }

            if (prefab != null)
            {
                Transform transform = instance.transform;
                transform.localPosition = prefab.transform.localPosition;
                transform.localRotation = prefab.transform.localRotation;
                transform.localScale = prefab.transform.localScale;
            }

            return Wrap(instance);
        }

        /// <summary>
        /// 以外部预制体在指定姿态同步获取组件租约。
        /// </summary>
        /// <param name="prefab">预制体。</param>
        /// <param name="position">位置。</param>
        /// <param name="rotation">旋转。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="useLocalPosition">是否使用本地位置而不是世界位置。</param>
        /// <returns>组件池化租约。</returns>
        public static T Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null, bool useLocalPosition = false)
        {
            T pooled = Spawn(prefab, parent);
            if (pooled == null)
            {
                return null;
            }

            if (useLocalPosition)
            {
                pooled.GameObject.transform.SetLocalPositionAndRotation(position, rotation);
            }
            else
            {
                pooled.GameObject.transform.SetPositionAndRotation(position, rotation);
            }

            return pooled;
        }

        /// <summary>
        /// 按资源地址异步获取组件租约。
        /// </summary>
        /// <param name="location">资源地址。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件池化租约。</returns>
        public new static async UniTask<T> SpawnAsync(string location, Transform parent = null, CancellationToken cancellationToken = default) =>
            Wrap(await GameObjectPoolService.SpawnAsync(location, parent, cancellationToken));

        /// <summary>
        /// 设置包装器池最大容量。
        /// </summary>
        /// <param name="size">最大容量。</param>
        public new static void SetMaxSize(int size)
        {
            s_Pool.MaxSize = size;
        }

        #endregion

        #region 保护方法 [PROTECTED METHODS]

        /// <summary>
        /// 初始化：确保目标组件已缓存。
        /// </summary>
        protected override void Init()
        {
            base.Init();
            _cache = null;
            ResolveComponent();
        }

        /// <summary>
        /// 解析并缓存目标组件。子类可覆写以自定义查找与缓存类型。
        /// </summary>
        protected virtual void ResolveComponent()
        {
            _cache = GetOrAddUserData<ComponentCache>();
            if (_cache == null)
            {
                _cache = new ComponentCache();
                SetUserData(_cache);
            }

            if (!_cache.Component && GameObject != null)
            {
                _cache.Component = GameObject.GetOrAddComponent<TComponent>();
            }
        }

        /// <summary>
        /// 将组件包装器归还专属对象池。
        /// </summary>
        protected override void ReturnToPool()
        {
            s_Pool.Release((T)this);
        }

        #endregion
    }

    /// <summary>
    /// 通用组件池化租约（无自定义逻辑的默认形态）。
    /// <para><see cref="GameObjectPoolService.SpawnPooled{TComponent}"/> 的返回类型。</para>
    /// </summary>
    /// <typeparam name="TComponent">目标组件类型。</typeparam>
    public sealed class Pooled<TComponent> : PooledComponent<Pooled<TComponent>, TComponent>
        where TComponent : Component
    {
    }
}
