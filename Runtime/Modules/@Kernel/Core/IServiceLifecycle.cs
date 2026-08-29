namespace Moirai.Atropos
{
    /// <summary>
    /// 运行时注册/注销时的生命周期回调。
    /// <para>实现此接口的服务在运行时注册时由 <see cref="ServiceScope"/> 调用 <see cref="Initialize"/>，
    /// 注销时调用 <see cref="Destroy"/>。所有注册（<see cref="GameServices.RegisterService"/>）均经过此接口——
    /// 注册时驱动 <c>OnInit</c>，注销时驱动 <c>Shutdown</c>。</para>
    /// </summary>
    internal interface IServiceLifecycle
    {
        /// <summary>
        /// 运行时注册后调用。触发 <c>OnInit</c>。
        /// </summary>
        /// <param name="scope">注册到的作用域。</param>
        void Initialize(ServiceScope scope);

        /// <summary>
        /// 运行时注销前调用。触发 <c>Shutdown</c>。
        /// </summary>
        void Destroy();
    }
}
