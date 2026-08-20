namespace Moirai.Atropos
{
    /// <summary>
    /// 作用域服务提供者。所有服务访问的统一入口。
    /// <para>纯 C# 服务通过构造函数注入此接口；非服务代码通过 <see cref="GameApp"/> 缓存属性（如 <see cref="GameApp.Audio"/>）访问。</para>
    /// </summary>
    public interface IServiceProvider
    {
        /// <summary>
        /// 获取服务（未找到抛 <see cref="GameException"/>）。
        /// </summary>
        T GetRequiredService<T>() where T : class;

        /// <summary>
        /// 获取服务（未找到返回 null）。
        /// </summary>
        T GetService<T>() where T : class;

        /// <summary>
        /// 尝试获取服务。
        /// </summary>
        bool TryGetService<T>(out T service) where T : class;
    }

    /// <summary>
    /// 作用域服务提供者实现。通过 parent 链实现跨作用域遮蔽查找（Gameplay → Scene → App）。
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
            if (_scope.TryGet<T>(out service)) return true;
            if (_parent != null && _parent.TryGetService<T>(out service)) return true;
            service = null;
            return false;
        }
    }
}
