namespace Moirai.Atropos
{
    /// <summary>
    /// AOT 安全的延迟服务解析器。替代 <c>Func&lt;T&gt;</c> 注入的 <c>MakeGenericMethod</c> 路径。
    /// <para>构造函数参数声明为 <c>IServiceResolver&lt;T&gt;</c> 时，容器直接 <c>new</c> 解析器实例——
    /// 零反射、零 <c>MakeGenericMethod</c>，IL2CPP 全量泛型共享下无风险。</para>
    /// <para>与 <c>Func&lt;T&gt;</c> 注入语义一致：延迟解析目标服务，拓扑建边保证委托调用时目标已就绪。</para>
    /// </summary>
    /// <typeparam name="T">目标服务契约类型（引用类型）。</typeparam>
    public interface IServiceResolver<out T> where T : class
    {
        /// <summary>
        /// 解析目标服务。目标未注册或已随作用域关闭时抛出 <see cref="GameException"/>。
        /// </summary>
        T Resolve();
    }
}
