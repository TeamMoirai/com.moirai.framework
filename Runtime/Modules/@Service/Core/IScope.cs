using System;

namespace Moirai.Atropos
{
    public interface IScope { }
    public sealed class AppScope : IScope { }
    public sealed class SceneScope : IScope { }
    public sealed class GameplayScope : IScope { }

    internal static class ScopeKindCache<TScope> where TScope : IScope
    {
        public static readonly EServiceScopeKind Scope = Resolve();

        private static EServiceScopeKind Resolve()
        {
            if (typeof(TScope) == typeof(AppScope)) return EServiceScopeKind.App;
            if (typeof(TScope) == typeof(SceneScope)) return EServiceScopeKind.Scene;
            if (typeof(TScope) == typeof(GameplayScope)) return EServiceScopeKind.Gameplay;
            throw new InvalidOperationException(StringUtility.Format("Unsupported scope type: {0}", typeof(TScope).FullName));
        }
    }
}
