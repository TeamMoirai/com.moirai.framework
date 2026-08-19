using UnityEngine;

namespace Moirai.Atropos
{
    public abstract class ServiceMonoBase : MonoBehaviour, IService
    {
        private GameServices.ServiceContext _context;

        public virtual int Priority => 0;
        public abstract EServiceScopeKind Scope { get; }
        public abstract void OnInit();
        public abstract void Shutdown();

        protected T Require<T>() where T : class => _context.Require<T>();
        protected bool TryGet<T>(out T service) where T : class => _context.TryGet(out service);
        protected T RequireApp<T>() where T : class => _context.RequireApp<T>();
        protected T RequireScene<T>() where T : class => _context.RequireScene<T>();
        protected T RequireGameplay<T>() where T : class => _context.RequireGameplay<T>();

        internal void SetContext(GameServices.ServiceContext context) => _context = context;
    }

    public abstract class ServiceMono<TScope> : ServiceMonoBase where TScope : IScope
    {
        public override EServiceScopeKind Scope => ScopeKindCache<TScope>.Scope;

        /// <summary>
        /// 注册合约接口类型。子类应覆写并返回业务接口（如 typeof(IMyService)），
        /// 默认值 <see cref="System.Object.GetType()"/> 表示未指定，将退回注册为 <see cref="IService"/> 合约
        /// （多个实例会争抢该合约，仅第一个生效）。
        /// </summary>
        protected virtual System.Type RegisterAs => GetType();

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
    }
}
