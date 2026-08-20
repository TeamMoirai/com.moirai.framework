using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 服务基类。用于需要 MonoBehaviour 生命周期的服务（如 Input、Resource Driver）。
    /// DI 辅助方法与 <see cref="ServiceBase"/> 中的实现完全一致——两者无法共享基类，
    /// 刻意保持重复以避免组合带来的间接调用开销。
    /// </summary>
    public abstract class ServiceMonoBase : MonoBehaviour, IService
    {
        #region 字段 [FIELDS]

        private GameServices.ServiceContext _context;

        #endregion

        #region 属性 [PROPERTIES]

        public virtual int Priority => 0;
        public abstract EServiceScopeKind Scope { get; }

        /// <summary>当前生命周期状态。</summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        /// <summary>
        /// 此服务依赖的合约接口类型列表。注册时验证依赖已就绪，未满足则抛出 <see cref="GameException"/>。
        /// 默认为空。子类覆写以声明依赖（如 <c>typeof(IResourceService)</c>）。
        /// 建议以 static readonly 数组返回，避免每次访问分配。
        /// </summary>
        public virtual Type[] Dependencies => Array.Empty<Type>();

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void Shutdown();

        #endregion

        #region 跨服务依赖 [CROSS-SERVICE DEPENDENCIES]

        /// <summary>获取依赖服务（查找顺序：当前作用域 → GetBest 回退）。未找到抛 <see cref="GameException"/>。</summary>
        protected T Require<T>() where T : class => _context.Require<T>();

        /// <summary>尝试获取依赖服务。</summary>
        protected bool TryGet<T>(out T service) where T : class => _context.TryGet(out service);

        /// <summary>从 App 作用域获取服务。未找到抛 <see cref="GameException"/>。</summary>
        protected T RequireApp<T>() where T : class => _context.RequireApp<T>();

        /// <summary>从 Scene 作用域获取服务。未找到抛 <see cref="GameException"/>。</summary>
        protected T RequireScene<T>() where T : class => _context.RequireScene<T>();

        /// <summary>从 Gameplay 作用域获取服务。未找到抛 <see cref="GameException"/>。</summary>
        protected T RequireGameplay<T>() where T : class => _context.RequireGameplay<T>();

        #endregion

        internal void SetContext(GameServices.ServiceContext context) => _context = context;
    }

    /// <summary>
    /// 泛型 MonoBehaviour 服务基类。Awake 时按 <see cref="RegisterAs"/> 指定的合约接口自动注册。
    /// </summary>
    /// <typeparam name="TScope">作用域标记类型（<see cref="AppScope"/> / <see cref="SceneScope"/> / <see cref="GameplayScope"/>）。</typeparam>
    public abstract class ServiceMono<TScope> : ServiceMonoBase where TScope : IScope
    {
        /// <summary>由 <typeparamref name="TScope"/> 推导的作用域种类。</summary>
        public override EServiceScopeKind Scope => ScopeKindCache<TScope>.Scope;

        /// <summary>
        /// 注册合约接口类型。子类应覆写并返回业务接口（如 <c>typeof(IMyService)</c>）。
        /// 未覆写时退回注册为 <see cref="IService"/> 合约（多个实例会争抢该合约，仅第一个生效）。
        /// </summary>
        protected virtual System.Type RegisterAs => GetType();

        #region 引擎方法 [UNITY METHODS]

        protected virtual void Awake()
        {
            if (Scope == EServiceScopeKind.App)
                DontDestroyOnLoad(gameObject);

            var contract = RegisterAs;
            if (contract != null && contract.IsInterface && contract != typeof(IService))
                GameServices.RegisterService(this, contract);
            else
                GameServices.RegisterService(this, typeof(IService));
        }

        protected virtual void OnDestroy()
        {
            GameServices.UnregisterService(this);
        }

        #endregion
    }
}
