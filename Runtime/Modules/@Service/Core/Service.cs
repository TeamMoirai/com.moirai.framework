using Cysharp.Threading.Tasks;

namespace Moirai.Atropos
{
    public interface IService
    {
        int Priority { get; }
        ServiceScopeKind Scope { get; }
        void OnInit();
        void Shutdown();
    }

    public interface IServiceTickable
    {
        void Tick(float elapseSeconds, float realElapseSeconds);
    }

    public interface IServiceFixedTickable
    {
        void FixedTick(float elapseSeconds, float realElapseSeconds);
    }

    public interface IServiceLateTickable
    {
        void LateTick(float elapseSeconds, float realElapseSeconds);
    }

    public interface IServiceGizmoDrawable
    {
        void OnDrawGizmos();
    }

    public interface IAsyncInitService
    {
        UniTask OnInitAsync();
    }

    public abstract class ServiceBase : IService
    {
        private ServiceSystem.ServiceContext _context;

        public virtual int Priority => 0;
        public virtual ServiceScopeKind Scope => ServiceScopeKind.App;
        public abstract void OnInit();
        public abstract void Shutdown();

        protected T Require<T>() where T : class => _context.Require<T>();
        protected bool TryGet<T>(out T service) where T : class => _context.TryGet(out service);
        protected T RequireApp<T>() where T : class => _context.RequireApp<T>();
        protected T RequireScene<T>() where T : class => _context.RequireScene<T>();
        protected T RequireGameplay<T>() where T : class => _context.RequireGameplay<T>();

        internal void SetContext(ServiceSystem.ServiceContext context) => _context = context;
    }
}
