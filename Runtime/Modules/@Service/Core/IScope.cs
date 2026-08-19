using System;

namespace Moirai.Atropos
{
    public interface IScope { }
    public sealed class AppScope : IScope { }
    public sealed class SceneScope : IScope { }
    public sealed class GameplayScope : IScope { }

    internal static class ScopeKindCache<TScope> where TScope : IScope
    {
        public static readonly ServiceScopeKind Scope = Resolve();

        private static ServiceScopeKind Resolve()
        {
            if (typeof(TScope) == typeof(AppScope)) return ServiceScopeKind.App;
            if (typeof(TScope) == typeof(SceneScope)) return ServiceScopeKind.Scene;
            if (typeof(TScope) == typeof(GameplayScope)) return ServiceScopeKind.Gameplay;
            throw new InvalidOperationException(StringUtility.Format("Unsupported scope type: {0}", typeof(TScope).FullName));
        }
    }
}
