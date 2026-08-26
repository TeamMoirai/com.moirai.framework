using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 服务基类。Awake 自动注册到指定作用域，OnDestroy 自动注销。
    /// <para>适用于需要 Unity 生命周期（Update/FixedUpdate/LateUpdate/碰撞/协程）的 Gameplay 层服务。</para>
    /// <para>不可实现 <see cref="IServiceTickable"/> 等 Tick 接口——Mono 服务由 Unity 自身生命周期驱动。</para>
    /// <para>重复注册自动销毁 GameObject（同契约幂等）。</para>
    /// </summary>
    /// <typeparam name="TScope">作用域标记类型。</typeparam>
    public abstract class ServiceMono<TScope> : MonoBehaviour, IService, IServiceLifecycle
        where TScope : IServiceScope, new()
    {
        [NonSerialized] private ServiceWorld _world;
        [NonSerialized] private ServiceScope _scope;

        /// <summary>
        /// 本实例是否已完成作用域注册——未注册的重复副本在 OnDestroy 中不得触碰注册表。
        /// </summary>
        [NonSerialized] private bool _registeredToScope;

        /// <summary>
        /// 当前生命周期状态。
        /// </summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        /// <summary>
        /// 轮询优先级（降序，高优先先 Tick）。Mono 服务不参与容器 Tick，此属性仅用于诊断。
        /// </summary>
        public virtual int Priority => 0;

        /// <summary>
        /// 所属作用域种类。
        /// </summary>
        public virtual EServiceScopeKind Scope => ScopeKindCache<TScope>.Kind;

        #region 运行时依赖查找 [RUNTIME DEPENDENCY LOOKUP]

        /// <summary>
        /// 在当前作用域中查找服务（未找到抛 <see cref="GameException"/>）。
        /// </summary>
        protected T Require<T>() where T : class
            => _world.GetRequiredService<T>();

        /// <summary>
        /// 在当前作用域中尝试查找服务。
        /// </summary>
        protected bool TryGet<T>(out T service) where T : class
            => _world.TryGetService(out service);

        /// <summary>
        /// 要求 App 作用域中的服务。
        /// </summary>
        protected T RequireApp<T>() where T : class
            => _world.GetRequiredServiceInScope<T>(EServiceScopeKind.App);

        /// <summary>
        /// 要求 Scene 作用域中的服务。
        /// </summary>
        protected T RequireScene<T>() where T : class
            => _world.GetRequiredServiceInScope<T>(EServiceScopeKind.Scene);

        /// <summary>
        /// 要求 Gameplay 作用域中的服务。
        /// </summary>
        protected T RequireGameplay<T>() where T : class
            => _world.GetRequiredServiceInScope<T>(EServiceScopeKind.Gameplay);

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 注册完成后调用（同步）。
        /// </summary>
        public abstract void OnInit();

        /// <summary>
        /// 注销或作用域关闭时调用。
        /// </summary>
        public abstract void Shutdown();

        #endregion

        #region IServiceLifecycle 实现 [RUNTIME LIFECYCLE]

        void IServiceLifecycle.Initialize(ServiceWorld world, ServiceScope scope)
        {
            if (State >= EServiceState.Initialized) return;

            _world = world;
            _scope = scope;
            OnInit();
            State = EServiceState.Initialized;
            GameServices.InvokeRegistered(this, GetType(), scope.Kind);
        }

        void IServiceLifecycle.Destroy()
        {
            if (State >= EServiceState.ShuttingDown) return;

            State = EServiceState.ShuttingDown;
            GameServices.InvokeShutdown(this);
            try { Shutdown(); }
            catch (System.Exception ex) { LogUtility.Error(ex.ToString()); }
            State = EServiceState.Disposed;
        }

        #endregion

        #region Unity 生命周期 [UNITY LIFECYCLE]

        /// <summary>
        /// 是否 DontDestroyOnLoad。App 作用域默认 true，Scene/Gameplay 默认 false。
        /// <para>隐藏基类 <see cref="UnityEngine.Object.DontDestroyOnLoad(Object)"/> 静态方法（CS0108）。</para>
        /// </summary>
        protected new virtual bool DontDestroyOnLoad => Scope == EServiceScopeKind.App;

        protected virtual void Awake()
        {
            EServiceScopeKind kind = ScopeKindCache<TScope>.Kind;

            // 同契约已被其他实例注册——多余副本自毁且不参与注销（同契约幂等，防止副本 OnDestroy 注销活实例）
            ServiceWorld world = GameServices.GetWorldInternal();
            if (world != null
                && world.TryGetScope(kind, out ServiceScope targetScope)
                && targetScope.TryGet(GetType(), out IService existing)
                && !ReferenceEquals(existing, this))
            {
                Destroy(gameObject);
                return;
            }

            // App 作用域跨场景存活（原 db62017 契约）；DontDestroyOnLoad 仅 Play 模式合法
            if (Application.isPlaying && DontDestroyOnLoad)
            {
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
            }

            // 以运行时具体类型为契约注册——基类泛型参数是开放类型，不能作为契约键；
            // 显式 Type 重载同时让子类的 [ServiceDependency] 依赖链自动预注册
            GameServices.RegisterService(kind, GetType(), this);
            _registeredToScope = true;
        }

        protected virtual void OnDestroy()
        {
            if (!_registeredToScope) return;

            if (State < EServiceState.ShuttingDown)
            {
                EServiceScopeKind kind = ScopeKindCache<TScope>.Kind;
                GameServices.UnregisterService(kind, GetType());
            }
        }

        #endregion
    }

    /// <summary>
    /// 作用域标记接口。
    /// </summary>
    public interface IServiceScope
    {
        /// <summary>
        /// 作用域种类。
        /// </summary>
        EServiceScopeKind Kind { get; }
    }

    /// <summary>
    /// App 作用域标记。
    /// </summary>
    public sealed class AppScope : IServiceScope
    {
        /// <inheritdoc />
        public EServiceScopeKind Kind => EServiceScopeKind.App;
    }

    /// <summary>
    /// Scene 作用域标记。
    /// </summary>
    public sealed class SceneScope : IServiceScope
    {
        /// <inheritdoc />
        public EServiceScopeKind Kind => EServiceScopeKind.Scene;
    }

    /// <summary>
    /// Gameplay 作用域标记。
    /// </summary>
    public sealed class GameplayScope : IServiceScope
    {
        /// <inheritdoc />
        public EServiceScopeKind Kind => EServiceScopeKind.Gameplay;
    }

    /// <summary>
    /// 作用域优先级常量。数值越小越先初始化、越后关闭。
    /// </summary>
    public static class ServiceScopeOrder
    {
        /// <summary>
        /// App 作用域优先级（全局，生命周期最长）。
        /// </summary>
        public const int APP = -10000;

        /// <summary>
        /// Scene 作用域优先级（场景卸载时重置）。
        /// </summary>
        public const int SCENE = -5000;

        /// <summary>
        /// Gameplay 作用域优先级（单局玩法）。
        /// </summary>
        public const int GAMEPLAY = 0;

        /// <summary>
        /// 将 <see cref="EServiceScopeKind"/> 映射到优先级常量。
        /// </summary>
        public static int FromKind(EServiceScopeKind kind) => kind switch
        {
            EServiceScopeKind.App => APP,
            EServiceScopeKind.Scene => SCENE,
            EServiceScopeKind.Gameplay => GAMEPLAY,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>
    /// 作用域类型缓存（泛型静态字段，编译期确定）。
    /// </summary>
    internal static class ScopeKindCache<TScope> where TScope : IServiceScope, new()
    {
        /// <summary>
        /// 作用域种类。
        /// </summary>
        public static readonly EServiceScopeKind Kind = new TScope().Kind;
    }
}
