using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Pool;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 池化组件租约：在 <see cref="PooledGameObject"/> 之上缓存目标组件，跨池复用保留（存于 Slot.UserData）。
    /// <para>通用场景请用 <see cref="Pooled{TComponent}"/>；需要自定义 Init / 组件解析时继承本类型（CRTP）。</para>
    /// <para>UserData 为单消费者槽位：异种占用时缓存降级为非驻留，不覆盖原数据。</para>
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
        /// 获取缓存的目标组件。租约失效或未解析时为 null。
        /// </summary>
        public TComponent Component => Cache != null ? Cache.Component : null;

        private ComponentCache _cache;
        /// <summary>
        /// 获取组件缓存（仅读取已解析结果；创建职责在 <see cref="ResolveComponent"/>）。
        /// </summary>
        protected ComponentCache Cache
        {
            get
            {
                if (_cache == null)
                {
                    _cache = GetUserData<ComponentCache>();
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
            if (!GameObjectPoolService.TryResolveInstance(instance, out RuntimeGameObjectPool pool, out int slotIndex))
            {
                return null;
            }

            if (!pool.TryBindLease(slotIndex, out uint generation, out GameObject bound, out Transform transform))
            {
                return null;
            }

            T pooled = s_Pool.Get();
            pooled.Bind(pool, slotIndex, generation, bound, transform);
            return pooled;
        }

        /// <summary>
        /// 按池化来源同步获取组件租约。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <returns>组件池化租约。</returns>
        public new static T Spawn(GameObjectPoolSource source, Transform parent = null) =>
            Wrap(GameObjectPoolService.Spawn(source, parent));

        /// <summary>
        /// 按池化来源在指定姿态同步获取组件租约。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="position">位置。</param>
        /// <param name="rotation">旋转。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="useLocalPosition">是否使用本地位置而不是世界位置。</param>
        /// <returns>组件池化租约。</returns>
        public static T Spawn(
            GameObjectPoolSource source,
            Vector3 position,
            Quaternion rotation,
            Transform parent = null,
            bool useLocalPosition = false) =>
            Wrap(GameObjectPoolService.Spawn(source, position, rotation, parent, useLocalPosition));

        /// <summary>
        /// 按池化来源异步获取组件租约。
        /// </summary>
        /// <param name="source">池化来源。</param>
        /// <param name="parent">父级 Transform。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>组件池化租约。</returns>
        public new static async UniTask<T> SpawnAsync(GameObjectPoolSource source, Transform parent = null, CancellationToken cancellationToken = default) =>
            Wrap(await GameObjectPoolService.SpawnAsync(source, parent, cancellationToken));

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
            if (TryResolveResidentCache<ComponentCache>(out ComponentCache cache))
            {
                if (!cache.Component && GameObject != null)
                {
                    cache.Component = GameObject.GetOrAddComponent<TComponent>();
                }

                return;
            }

            // 非驻留降级：异种占用 UserData 或无槽位，每租期解析。
            ComponentCache transient = cache ?? new ComponentCache();
            if (!transient.Component && GameObject != null)
            {
                transient.Component = GameObject.GetOrAddComponent<TComponent>();
            }

            Cache = transient;
        }

        /// <summary>
        /// 尝试获取驻留 UserData 缓存。异种占用时不覆盖原数据，返回 false 并给出非驻落实例。
        /// </summary>
        /// <typeparam name="TCache">缓存类型。</typeparam>
        /// <param name="cache">驻留缓存；失败时为新建的非驻落实例（可为 null）。</param>
        /// <returns>是否驻留成功。</returns>
        protected bool TryResolveResidentCache<TCache>(out TCache cache) where TCache : ComponentCache, new()
        {
            TCache existing = GetOrAddUserData<TCache>();
            if (existing != null)
            {
                cache = existing;
                Cache = existing;
                return true;
            }

            // Slot.UserData 被异种类型占用：不覆盖，降级非驻留。
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GetUserData<object>() != null)
            {
                LogUtility.Warning("[GameObjectPool] Slot.UserData occupied by alien type; component cache is non-resident: {0}", typeof(TCache).Name);
            }
#endif
            cache = new TCache();
            Cache = cache;
            return false;
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
