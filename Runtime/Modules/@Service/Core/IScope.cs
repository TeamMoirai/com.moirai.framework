using System;

namespace Moirai.Atropos
{
    /// <summary>作用域标记类型接口。用于 <see cref="ServiceMono{TScope}"/> 编译期确定 <see cref="EServiceScopeKind"/>。</summary>
    public interface IScope { }

    /// <summary>App 作用域标记。</summary>
    public sealed class AppScope : IScope { }

    /// <summary>Scene 作用域标记。</summary>
    public sealed class SceneScope : IScope { }

    /// <summary>Gameplay 作用域标记。</summary>
    public sealed class GameplayScope : IScope { }

    /// <summary>
    /// 作用域优先级常量。数值越小越先初始化、越后关闭。
    /// <para>用于 <see cref="ServiceScope.Order"/> 排序，替代隐式枚举值比较。</para>
    /// </summary>
    public static class ServiceScopeOrder
    {
        /// <summary>App 作用域优先级（全局，生命周期最长）。</summary>
        public const int App = -10000;

        /// <summary>Scene 作用域优先级（场景卸载时重置）。</summary>
        public const int Scene = -5000;

        /// <summary>Gameplay 作用域优先级（单局玩法）。</summary>
        public const int Gameplay = 0;

        /// <summary>将 <see cref="EServiceScopeKind"/> 映射到优先级常量。</summary>
        public static int FromKind(EServiceScopeKind kind) => kind switch
        {
            EServiceScopeKind.App => App,
            EServiceScopeKind.Scene => Scene,
            EServiceScopeKind.Gameplay => Gameplay,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>
    /// 泛型作用域标记 → <see cref="EServiceScopeKind"/> 映射缓存。
    /// 利用泛型静态字段实现一次性映射，避免每次访问 <c>ServiceMono&lt;TScope&gt;.Scope</c> 时重复 typeof 比较。
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
