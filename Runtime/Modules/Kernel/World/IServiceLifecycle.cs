namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期驱动接口（内部）。生命周期转换只存在于此一处——
    /// 状态机由容器（<see cref="ServiceWorld"/>/<see cref="ServiceScope"/>）经本接口统一驱动，
    /// 服务侧 <see cref="ServiceBase.State"/> 仅为只读投影。
    /// </summary>
    internal interface IServiceLifecycle
    {
        /// <summary>
        /// 初始化驱动。触发 <c>OnInit</c>（世界初始化拓扑序位置或运行时注册点）。
        /// </summary>
        /// <param name="world">所属世界（拦截器通知来源）。</param>
        /// <param name="scope">注册到的作用域。</param>
        void Initialize(ServiceWorld world, ServiceScope scope);

        /// <summary>
        /// 关闭驱动。触发 <c>OnShutdown</c>（注销或作用域关闭时，严格逆初始化序）。
        /// </summary>
        /// <param name="world">所属世界（拦截器通知来源）。</param>
        void Destroy(ServiceWorld world);
    }
}
