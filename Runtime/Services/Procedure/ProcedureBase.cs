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
        protected void ChangeState<T>() where T : ProcedureBase
        {
            Owner?.ChangeState<T>();
        }
    }
}
