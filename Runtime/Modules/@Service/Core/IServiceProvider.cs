namespace Moirai.Atropos
{
    /// <summary>
    /// 作用域服务提供者。所有服务访问的统一入口。
    /// <para>纯 C# 服务通过构造函数注入此接口；非服务代码通过 <see cref="GameApp.Services"/> 访问。</para>
    /// <para><b>线程契约</b>：仅限 Unity 主线程调用。</para>
    /// </summary>
    public interface IServiceProvider
    {
        /// <summary>获取服务（未找到抛 <see cref="GameException"/>）。</summary>
        T GetRequiredService<T>() where T : class;

        /// <summary>获取服务（未找到返回 null）。</summary>
        T GetService<T>() where T : class;

        /// <summary>尝试获取服务。</summary>
        bool TryGetService<T>(out T service) where T : class;
    }
}

namespace Moirai.Atropos
{
    /// <summary>
    /// 作用域服务提供者实现。通过 parent 链实现跨作用域遮蔽查找（Gameplay → Scene → App）。
    /// <para>替代旧版全局服务字典 + 跨作用域绑定表的设计，消除全局可变状态。</para>
    /// </summary>
    internal sealed class ScopedServiceProvider : IServiceProvider
    {
        private readonly ServiceScope _scope;
        private readonly ScopedServiceProvider _parent;

        internal ScopedServiceProvider(ServiceScope scope, ScopedServiceProvider parent)
        {
            _scope = scope;
            _parent = parent;
        }

        public T GetRequiredService<T>() where T : class
        {
            if (TryGetService<T>(out var service)) return service;
            throw new GameException(
                StringUtility.Format("Service '{0}' not found in scope '{1}' or parent scopes.",
                    typeof(T).FullName, _scope.Kind));
        }

        public T GetService<T>() where T : class
            => TryGetService<T>(out var service) ? service : null;

        public bool TryGetService<T>(out T service) where T : class
        {
            // 当前作用域优先
            if (_scope.TryGet<T>(out service)) return true;
            // 回退到父作用域（遮蔽链）
            if (_parent != null && _parent.TryGetService<T>(out service)) return true;
            service = null;
            return false;
        }
    }
}
