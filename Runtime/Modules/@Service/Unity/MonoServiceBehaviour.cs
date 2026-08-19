using UnityEngine;

namespace Moirai.Atropos
{
    public abstract class MonoServiceBehaviourBase : MonoBehaviour, IService
    {
        private ServiceSystem.ServiceContext _context;

        public virtual int Priority => 0;
        public abstract ServiceScopeKind Scope { get; }
        public abstract void OnInit();
        public abstract void Shutdown();

        protected T Require<T>() where T : class => _context.Require<T>();
        protected bool TryGet<T>(out T service) where T : class => _context.TryGet(out service);
        protected T RequireApp<T>() where T : class => _context.RequireApp<T>();
        protected T RequireScene<T>() where T : class => _context.RequireScene<T>();
        protected T RequireGameplay<T>() where T : class => _context.RequireGameplay<T>();

        internal void SetContext(ServiceSystem.ServiceContext context) => _context = context;
    }

    public abstract class MonoServiceBehaviour<TScope> : MonoServiceBehaviourBase where TScope : IScope
    {
        public override ServiceScopeKind Scope => ScopeKindCache<TScope>.Scope;

        protected virtual System.Type RegisterAs => GetType();

        protected virtual void Awake()
        {
            if (Scope == ServiceScopeKind.App)
                DontDestroyOnLoad(gameObject);
            ServiceSystem.RegisterService<IService>(this);
        }

        protected virtual void OnDestroy()
        {
            ServiceSystem.UnregisterService(this);
        }
    }
}
