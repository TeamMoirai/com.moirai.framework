using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 自注册 MonoBehaviour 服务基类。
    /// <para>在 <see cref="Awake"/> 中自动注册到 <typeparamref name="TScope"/> 对应的作用域，
    /// <see cref="OnDestroy"/> 中自动注销。适用于快速原型、Inspector 驱动配置等
    /// 不需要容器管理生命周期的场景。</para>
    /// <para>与 <see cref="ServiceMono{TScope}"/>（容器通过 <c>AddComponent</c> 创建）互斥——
    /// 选择其一即可：<see cref="SelfRegisteringMono{TScope}"/> 自管 GameObject 生命周期，
    /// <see cref="ServiceMono{TScope}"/> 由容器创建 GameObject。</para>
    /// </summary>
    /// <typeparam name="TScope">作用域标记（<see cref="AppScope"/> / <see cref="SceneScope"/> / <see cref="GameplayScope"/>）。</typeparam>
    public abstract class SelfRegisteringMono<TScope> : ServiceMonoBase where TScope : IScope
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 由 <typeparamref name="TScope"/> 推导的作用域种类。
        /// </summary>
        public override EServiceScopeKind Scope => ScopeKindCache<TScope>.Scope;

        #endregion

        #region Unity 生命周期 [UNITY LIFECYCLE]

        /// <summary>
        /// 自动注册到对应作用域。子类覆写时须调用 <c>base.Awake()</c>。
        /// </summary>
        protected virtual void Awake()
        {
            var world = GameServices.GetWorldInternal();
            if (world == null)
            {
                LogUtility.Error("Cannot self-register: ServiceWorld is not initialized. " +
                    "Ensure GameApp is active before instantiating SelfRegisteringMono.");
                return;
            }

            var kind = ScopeKindCache<TScope>.Scope;
            var scope = world.EnsureScope(kind);

            // 去重：同契约已注册则销毁自身（避免多实例覆盖）
            if (scope.TryGet(GetType(), out _))
            {
                LogUtility.Warning("SelfRegisteringMono: contract '{0}' is already registered; destroying duplicate GameObject.",
                    GetType().FullName);
                Destroy(gameObject);
                return;
            }

            scope.RegisterRuntime(this);
        }

        /// <summary>
        /// 自动从作用域注销。子类覆写时须调用 <c>base.OnDestroy()</c>。
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (!IsInitialized) return;

            var world = GameServices.GetWorldInternal();
            if (world == null) return;

            if (world.TryGetScope(ScopeKindCache<TScope>.Scope, out var scope))
                scope.UnregisterRuntime(GetType());
        }

        #endregion
    }
}
