namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程基类 — 自包含的生命周期抽象，不依赖外部状态机。
    /// </summary>
    /// <remarks>
    /// 资源作用域：派生类经 <see cref="Res"/> 惰性加载本状态所需资产（<c>Res.Load&lt;T&gt;("location")</c>），
    /// 租约归状态所有——<see cref="OnLeave"/> 后由流程处理器（Owner）自动 <see cref="AssetLeaseScope.Dispose"/>，
    /// 状态持有的资产生命周期 = 状态生命周期，无需手工逐租约释放。
    /// </remarks>
    public abstract class ProcedureBase
    {
        /// <summary>流程处理器引用，由 <see cref="ProcedureServiceHandler.Initialize"/> 时注入。</summary>
        internal IProcedureService Owner { get; private set; }

        /// <summary>本状态的惰性租约作用域（首次 <see cref="Res"/> 访问时创建；Leave 后由 Owner 清理置空）。</summary>
        private AssetLeaseScope _res;

        /// <summary>
        /// 本状态的惰性资源作用域：OnEnter/OnUpdate 中按需 <c>Res.Load&lt;T&gt;("location")</c> 拉起资产，
        /// 离开状态时由流程处理器统一释放（幂等）。仅在主线程访问。
        /// </summary>
        protected AssetLeaseScope Res => _res ??= new AssetLeaseScope();

        /// <summary>
        /// Owner 在 OnLeave 之后调用：释放并置空本状态的租约作用域（internal——由流程处理器保证，派生类勿调）。
        /// </summary>
        internal void CleanupLeaseScope()
        {
            _res?.Dispose();
            _res = null;
        }

        internal void SetOwner(IProcedureService owner) => Owner = owner;

        /// <summary>
        /// 流程初始化时调用。
        /// </summary>
        protected internal virtual void OnInit()
        {
        }

        /// <summary>
        /// 进入流程时调用。
        /// </summary>
        protected internal virtual void OnEnter()
        {
        }

        /// <summary>
        /// 流程轮询时调用。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        protected internal virtual void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 离开流程时调用。
        /// </summary>
        /// <param name="isShutdown">是否是关闭流程管理器时触发。</param>
        protected internal virtual void OnLeave(bool isShutdown)
        {
        }

        /// <summary>
        /// 流程销毁时调用。
        /// </summary>
        protected internal virtual void OnDestroy()
        {
        }

        /// <summary>
        /// 切换到指定流程。
        /// </summary>
        /// <typeparam name="T">要切换到的流程类型。</typeparam>
        /// <exception cref="GameException">流程未注册进状态机（<c>Owner</c> 未注入）时抛出——
        /// 流程在 <see cref="ProcedureServiceHandler.Initialize"/> 之外被使用属时序错误，静默忽略会掩盖问题。</exception>
        protected void ChangeState<T>() where T : ProcedureBase
        {
            if (Owner == null)
            {
                throw new GameException(
                    "Procedure is not registered in an IProcedureService backend — ChangeState is unavailable before Initialize.");
            }

            Owner.ChangeState<T>();
        }
    }
}
