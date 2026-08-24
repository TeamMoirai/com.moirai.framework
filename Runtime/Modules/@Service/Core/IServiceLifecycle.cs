namespace Moirai.Atropos
{
    /// <summary>
    /// 运行时注册/注销时的生命周期回调。
    /// <para>实现此接口的服务在运行时注册时由 <see cref="ServiceScope"/> 调用 <see cref="Initialize"/>，
    /// 注销时调用 <see cref="Destroy"/>。构建期注册（<see cref="GameServices.BuildAsync"/>）不经过此接口——
    /// 其生命周期由 <c>BuildAsync</c> 统一驱动 <c>OnInit</c> / <c>OnInitAsync</c>。</para>
    /// </summary>
    internal interface IServiceLifecycle
    {
        /// <summary>
        /// 运行时注册后调用。注入服务提供者并触发 <c>OnInit</c>。
        /// </summary>
        /// <param name="world">所属服务世界。</param>
        /// <param name="scope">注册到的作用域。</param>
        void Initialize(ServiceWorld world, ServiceScope scope);

        /// <summary>
        /// 运行时注销前调用。触发 <c>Shutdown</c>。
        /// </summary>
        void Destroy();
    }
}
