namespace Moirai.Atropos
{
    /// <summary>
    /// 服务上下文：服务实例在生命周期内按契约接口解析依赖的注入面（Phase 2 注入互调）。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="ServiceWorld"/> 实现（容器自身即上下文）；经 <see cref="IServiceContextAware.SetContext"/> 在注册入图时注入服务实例， <br />
    /// 派生服务在 <c>OnInit</c> 中经基座 <c>Context</c> 属性消费——替代经静态门面的间接互调，为静态状态实例化（任务③）铺路。 <br />
    /// 解析键是契约类型：服务实现类经 <see cref="Moirai.Atropos.ProviderHandlerHostAttribute"/> 自报契约接口，注册时附加契约绑定。
    /// </remarks>
    public interface IServiceContext
    {
        /// <summary>
        /// 按契约接口解析依赖；未注册抛 <see cref="GameException"/>。
        /// </summary>
        T GetRequiredService<T>() where T : class;

        /// <summary>
        /// 按契约接口解析依赖；未注册返回 null。
        /// </summary>
        T GetService<T>() where T : class;

        /// <summary>
        /// 按契约接口尝试解析依赖。
        /// </summary>
        bool TryGetService<T>(out T service) where T : class;
    }

    /// <summary>
    /// 上下文注入标记：服务基座实现本接口，容器在注册入图时注入 <see cref="IServiceContext"/>。
    /// </summary>
    /// <remarks>
    /// 决定记录：不改 <see cref="IService.OnInit"/> 签名（契约面含异步变体，改签名是三接口 breaking）—— <br />
    /// 采用加法演进：基座 <c>ServiceBase</c>/<c>ServiceMono</c> 实现本接口并暴露 <c>Context</c> 属性，派生服务按需消费。
    /// </remarks>
    public interface IServiceContextAware
    {
        /// <summary>
        /// 容器在注册入图时注入（世界引用在两阶段注册期内即已确定）。
        /// </summary>
        void SetContext(IServiceContext context);
    }
}
