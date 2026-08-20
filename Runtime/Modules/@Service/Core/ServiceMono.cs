using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// MonoBehaviour 服务基类。用于需要 MonoBehaviour 生命周期的服务（如 Input、Resource Driver）。
    /// <para><b>依赖注入方式</b>：覆写 <see cref="Inject"/> 方法，通过 <see cref="IServiceProvider"/> 获取依赖。</para>
    /// <para>不自行注册——由 <see cref="ServiceContainer"/> 通过工厂创建并管理生命周期。</para>
    /// </summary>
    public abstract class ServiceMonoBase : MonoBehaviour, IService
    {
        #region 属性 [PROPERTIES]

        public virtual int Priority => 0;
        public abstract EServiceScopeKind Scope { get; }

        /// <summary>当前生命周期状态。由容器维护，子类只读。</summary>
        public EServiceState State { get; internal set; } = EServiceState.Created;

        #endregion

        #region 生命周期 [LIFECYCLE]

        public abstract void OnInit();
        public abstract void Shutdown();

        #endregion

        #region 依赖注入 [DEPENDENCY INJECTION]

        /// <summary>
        /// 容器在创建后、OnInit 前调用。子类覆写以获取依赖服务。
        /// <para>等价于纯 C# 服务的构造函数注入——MonoBehaviour 无法使用构造函数，因此使用此方法。</para>
        /// </summary>
        protected internal virtual void Inject(IServiceProvider provider) { }

        #endregion
    }

    /// <summary>
    /// 泛型 MonoBehaviour 服务基类。通过 <typeparamref name="TScope"/> 编译期确定作用域。
    /// </summary>
    /// <typeparam name="TScope">作用域标记（<see cref="AppScope"/> / <see cref="SceneScope"/> / <see cref="GameplayScope"/>）。</typeparam>
    public abstract class ServiceMono<TScope> : ServiceMonoBase where TScope : IScope
    {
        /// <summary>由 <typeparamref name="TScope"/> 推导的作用域种类。</summary>
        public override EServiceScopeKind Scope => ScopeKindCache<TScope>.Scope;

        // 注意：不覆写 Awake() 自行注册——由 ServiceContainer 工厂创建并调用 Inject() + OnInit()。
    }
}
