namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程基类 — 自包含的生命周期抽象，不依赖外部状态机。
    /// </summary>
    public abstract class ProcedureBase
    {
        /// <summary>
        /// 流程处理器引用，由 <see cref="ProcedureServiceHandler.Initialize"/> 时注入。
        /// </summary>
        internal ProcedureServiceHandler Owner { get; private set; }

        internal void SetOwner(ProcedureServiceHandler owner) => Owner = owner;

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
                    "Procedure is not registered in a ProcedureServiceHandler — ChangeState is unavailable before Initialize.");
            }

            Owner.ChangeState<T>();
        }
    }
}
