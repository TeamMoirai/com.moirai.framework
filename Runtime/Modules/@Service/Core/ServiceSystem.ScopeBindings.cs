namespace Moirai.Atropos
{
    public static partial class ServiceSystem
    {
        internal struct ScopeBindings
        {
            public IService App;
            public IService Scene;
            public IService Gameplay;

            public bool IsEmpty => App == null && Scene == null && Gameplay == null;

            public IService Get(ServiceScopeKind scope)
            {
                switch (scope)
                {
                    case ServiceScopeKind.App: return App;
                    case ServiceScopeKind.Scene: return Scene;
                    case ServiceScopeKind.Gameplay: return Gameplay;
                    default: return null;
                }
            }

            public IService GetBest()
            {
                if (Gameplay != null) return Gameplay;
                if (Scene != null) return Scene;
                return App;
            }
        }
    }
}
