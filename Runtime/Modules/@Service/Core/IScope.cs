using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 作用域标记类型接口。用于 <see cref="ServiceMono{TScope}"/> 编译期确定 <see cref="EServiceScopeKind"/>。
    /// </summary>
    public interface IScope { }

    /// <summary>App 作用域标记。</summary>
    public sealed class AppScope : IScope { }

    /// <summary>Scene 作用域标记。</summary>
    public sealed class SceneScope : IScope { }

    /// <summary>Gameplay 作用域标记。</summary>
    public sealed class GameplayScope : IScope { }

    /// <summary>
    /// 泛型作用域标记 → <see cref="EServiceScopeKind"/> 映射缓存。
    /// 利用泛型静态字段实现编译期类型 → 枚举的一次性映射，避免每次访问 ServiceMono{TScope}.Scope 时重复 typeof 比较。
    /// </summary>
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
