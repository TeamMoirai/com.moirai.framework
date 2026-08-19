namespace Moirai.Atropos
{
    public static partial class GameServices
    {
        internal struct ScopeBindings
        {
            public IService App;
            public IService Scene;
            public IService Gameplay;

            public bool IsEmpty => App == null && Scene == null && Gameplay == null;

            public IService Get(EServiceScopeKind scope)
            {
                switch (scope)
                {
                    case EServiceScopeKind.App: return App;
                    case EServiceScopeKind.Scene: return Scene;
                    case EServiceScopeKind.Gameplay: return Gameplay;
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
