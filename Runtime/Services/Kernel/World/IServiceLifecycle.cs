namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期 seam（内部）。生命周期转换与状态读取只存在于此一处——
    /// 状态机由容器（<see cref="ServiceWorld"/>/<see cref="ServiceScope"/>）经本接口统一驱动，
    /// 服务侧 <see cref="ServiceBase.State"/> 仅为只读投影。
    /// <para>本接口同时是<b>可注册的判据</b>：<see cref="ServiceWorld.Register"/> 据此拒绝任何
    /// 自行实现 <see cref="IService"/> 而不经 <see cref="ServiceBase"/>/<see cref="ServiceMono{TScope}"/>
    /// 派生的类型——容器读不到状态的服务永远判不出就绪（<see cref="ServiceWorld.IsServiceReady"/>），
    /// 其依赖方会在运行期注册时误报"依赖未初始化"。</para>
    /// <para>拦截器通知一律由容器发出，本接口不承担横切（见 <see cref="IServiceInterceptor"/>）。</para>
    /// </summary>
    internal interface IServiceLifecycle
    {
        /// <summary>
        /// 当前状态（容器侧读取口）。与 <see cref="ServiceWorld.IsServiceReady"/> 的判定同源，
        /// 因此"能否被注册"与"能否被读到状态"是同一个条件，不会各自漂移。
        /// </summary>
        EServiceState StateInternal { get; }

        /// <summary>
        /// 初始化驱动。触发 <c>OnInit</c>（世界初始化拓扑序位置或运行时注册点）。
        /// <para>只负责状态转换——契约级的拦截器通知由容器在 <c>OnInit</c> 返回后按注册句柄发出
        /// （<see cref="ServiceScope.ActivateService"/>），本接口不知道自己的注册契约。</para>
        /// </summary>
        void Initialize();

        /// <summary>
        /// 关闭驱动。触发 <c>OnShutdown</c>（注销或作用域关闭时，严格逆初始化序）。
        /// <para>幂等：已进入 <see cref="EServiceState.ShuttingDown"/> 及之后的状态直接返回。</para>
        /// </summary>
        void Destroy();
    }
}
