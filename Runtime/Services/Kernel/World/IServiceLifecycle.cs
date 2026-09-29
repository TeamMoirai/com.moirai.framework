namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期 seam（内部）：容器驱动状态转换与状态读取的唯一入口。
    /// </summary>
    /// <remarks>
    /// 状态机由容器（<see cref="ServiceWorld"/> / <see cref="ServiceScope"/>）经本接口统一驱动，服务侧 <see cref="ServiceBase.State"/> 仅为只读投影。
    /// 本接口同时是<b>可注册的判据</b>：<see cref="ServiceWorld.Register"/> 据此拒绝不经 <see cref="ServiceBase"/> / <see cref="ServiceMono{TScope}"/> 派生而自行实现 <see cref="IService"/> 的类型——容器读不到状态的服务永远判不出就绪（<see cref="ServiceWorld.IsServiceReady"/>）。
    /// 拦截器通知一律由容器发出，本接口不承担横切（见 <see cref="IServiceInterceptor"/>）。
    /// </remarks>
    internal interface IServiceLifecycle
    {
        /// <summary>
        /// 当前状态（容器侧读取口，与 <see cref="ServiceWorld.IsServiceReady"/> 的判定同源）。
        /// </summary>
        EServiceState StateInternal { get; }

        /// <summary>
        /// 初始化驱动：触发 <c>OnInit</c>（世界初始化拓扑序位置或运行时注册点）。
        /// </summary>
        /// <remarks>
        /// 只负责状态转换；契约级的拦截器通知由容器在 <c>OnInit</c> 返回后按注册句柄发出（<see cref="ServiceScope.ActivateService"/>），本接口不知道自己的注册契约。
        /// </remarks>
        void Initialize();

        /// <summary>
        /// 关闭驱动：触发 <c>OnShutdown</c>（注销或作用域关闭时，严格逆初始化序）。
        /// </summary>
        /// <remarks>
        /// 幂等：已进入 <see cref="EServiceState.ShuttingDown"/> 及之后的状态直接返回。
        /// </remarks>
        void Destroy();
    }
}
