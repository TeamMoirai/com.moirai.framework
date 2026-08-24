using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 服务基类。用于需要 MonoBehaviour 生命周期的服务。
    /// <para>依赖通过覆写 <see cref="Inject"/> 方法获取，或通过 <see cref="Require{T}"/> / <see cref="TryGet{T}"/> 运行时查找。</para>
    /// <para>由 <see cref="ServiceWorld"/> 通过 AddComponent 创建并管理生命周期。</para>
    /// </summary>
    public abstract class ServiceMonoBase : MonoBehaviour, IService, IServiceLifecycle
    {
        #region 属性 [PROPERTIES]

        public virtual int Priority => 0;
        public abstract EServiceScopeKind Scope { get; }

        /// <summary>
        /// 当前生命周期状态。由容器维护，子类只读。
        /// </summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        #endregion

        #region 运行时依赖查找 [RUNTIME DEPENDENCY LOOKUP]

        private IServiceProvider _serviceProvider;

        /// <summary>
        /// 在当前作用域中查找服务（未找到抛 <see cref="GameException"/>）。
        /// <para>按 Gameplay > Scene > App 优先级返回最优服务。</para>
        /// </summary>
        protected T Require<T>() where T : class
            => _serviceProvider.GetRequiredService<T>();

        /// <summary>
        /// 在当前作用域中尝试查找服务。
        /// <para>按 Gameplay > Scene > App 优先级返回最优服务。</para>
        /// </summary>
        protected bool TryGet<T>(out T service) where T : class
            => _serviceProvider.TryGetService<T>(out service);

        /// <summary>
        /// 要求 App 作用域中的服务。
        /// </summary>
        protected T RequireApp<T>() where T : class
            => _serviceProvider.GetRequiredServiceInScope<T>(EServiceScopeKind.App);

        /// <summary>
        /// 要求 Scene 作用域中的服务。
        /// </summary>
        protected T RequireScene<T>() where T : class
            => _serviceProvider.GetRequiredServiceInScope<T>(EServiceScopeKind.Scene);

        /// <summary>
        /// 要求 Gameplay 作用域中的服务。
        /// </summary>
        protected T RequireGameplay<T>() where T : class
            => _serviceProvider.GetRequiredServiceInScope<T>(EServiceScopeKind.Gameplay);

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void Shutdown();

        #endregion

        #region 依赖注入 [DEPENDENCY INJECTION]

        /// <summary>
        /// 构建器在创建后、OnInit 前调用。子类覆写以获取依赖服务。
        /// </summary>
        protected internal virtual void Inject(IServiceProvider provider) { }

        #endregion

        #region 内部初始化 [INTERNAL INITIALIZATION]

        /// <summary>
        /// 由 <see cref="ServiceWorld"/> 在构建期调用，注入服务提供者并触发 <see cref="Inject"/>。
        /// </summary>
        internal void InjectInternal(IServiceProvider provider)
        {
            _serviceProvider = provider;
            Inject(provider);
        }

        /// <summary>
        /// 运行时注册后是否已完成初始化。供 <see cref="SelfRegisteringMono{TScope}"/> 使用。
        /// </summary>
        protected bool IsInitialized { get; private set; }

        #endregion

        #region IServiceLifecycle 实现 [RUNTIME LIFECYCLE]

        void IServiceLifecycle.Initialize(ServiceWorld world, ServiceScope scope)
        {
            if (IsInitialized) return;

            InjectInternal(world);
            IsInitialized = true;
            OnInit();
            State = EServiceState.Initialized;
            GameServices.InvokeRegistered(this, GetType(), scope.Kind);
        }

        void IServiceLifecycle.Destroy()
        {
            if (!IsInitialized) return;

            State = EServiceState.ShuttingDown;
            GameServices.InvokeShutdown(this);
            try { Shutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }
            State = EServiceState.Disposed;
            IsInitialized = false;
        }

        #endregion
    }

    /// <summary>
    /// 泛型 MonoBehaviour 服务基类。通过 <typeparamref name="TScope"/> 编译期确定作用域。
    /// </summary>
    /// <typeparam name="TScope">作用域标记（<see cref="AppScope"/> / <see cref="SceneScope"/> / <see cref="GameplayScope"/>）。</typeparam>
    public abstract class ServiceMono<TScope> : ServiceMonoBase where TScope : IScope
    {
        /// <summary>
        /// 由 <typeparamref name="TScope"/> 推导的作用域种类。
        /// </summary>
        public override EServiceScopeKind Scope => ScopeKindCache<TScope>.Scope;
    }
}
