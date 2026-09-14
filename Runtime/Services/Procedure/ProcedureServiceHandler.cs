using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程处理器抽象基类（策略模式抽象策略）— 自包含状态机契约，不依赖外部 FSM 服务。
    /// <para>定义 <see cref="ProcedureService"/> 外观调用的流程状态机后端契约；<see cref="ProcedureBase"/> 子类经 <c>Owner</c> 回调本处理器。</para>
    /// <para>默认实现为 <see cref="DefaultProcedureHandler"/>，由 <see cref="ProcedureServiceSettings"/> 驱动初始化。</para>
    /// </summary>
    /// <remarks>
    /// <para><b>同步生命周期契约</b>：流程服务仅经 HandlerHost 生成的 <c>Handler</c> 属性驱动
    /// <see cref="FrameworkHandler.OnInit"/> / <see cref="FrameworkHandler.OnShutdown"/>（同步路径）；
    /// 基类的 <c>OnInitAsync</c> / <c>OnShutdownAsync</c> 不会被流程服务 await——需要异步就绪的后端
    /// 应在 <see cref="FrameworkHandler.OnInit"/> 内自管（如启动内部异步任务并在就绪前拒绝服务调用）。</para>
    /// <para><b>切换广播契约</b>：后端须在每次切换完成（<c>OnEnter</c> 返回后）调用
    /// <see cref="RecordTransition"/>——历史记录与 <see cref="ProcedureService.ProcedureChanged"/> 广播由基类统一承载；
    /// 关停切换（To 为 null）仅记入历史，不广播。广播期间 <see cref="IsBroadcastingTransition"/> 为 true，
    /// 后端 <c>StartProcedure</c>/<c>ChangeState</c> 须拒绝重入（抛 <see cref="GameException"/>）——
    /// OnEnter/OnLeave 内的嵌套切换发生在记录之前，不受该标志影响。</para>
    /// </remarks>
    [Serializable]
    public abstract class ProcedureServiceHandler : FrameworkHandler
    {
        /// <summary>切换历史上限（环形截断，超出即丢弃最旧记录）。</summary>
        private const int TransitionHistoryCapacity = 32;

        // 不做内联初始化——[SerializeReference] 重建不运行字段初始化器（域重载后为 null），统一经懒加载属性兜底
        [NonSerialized] private List<ProcedureTransitionRecord> _transitionHistory;
        [NonSerialized] private bool _isBroadcastingTransition;

        /// <summary>
        /// 切换历史存储（懒初始化，兼容 [SerializeReference] 重建语义）。
        /// </summary>
        private List<ProcedureTransitionRecord> TransitionHistoryList =>
            _transitionHistory ??= new List<ProcedureTransitionRecord>(TransitionHistoryCapacity);

        /// <summary>
        /// 状态机是否已就绪（已 <see cref="Initialize"/> 且未关停）。
        /// <para>未就绪时 <see cref="CurrentProcedure"/> 等查询会 fail-fast；消费方（调试器、Inspector）
        /// 应先经此属性守卫，避免轮询路径命中异常。</para>
        /// </summary>
        public abstract bool IsStateReady { get; }

        /// <summary>
        /// 当前流程。
        /// </summary>
        public abstract ProcedureBase CurrentProcedure { get; }

        /// <summary>
        /// 当前流程持续时间。
        /// </summary>
        public abstract float CurrentProcedureTime { get; }

        /// <summary>
        /// 已注册的全部流程（未初始化时为空集）。
        /// </summary>
        public abstract IReadOnlyCollection<ProcedureBase> Procedures { get; }

        /// <summary>
        /// 最近的流程切换历史（时间升序，容量 <see cref="TransitionHistoryCapacity"/>）。
        /// <para>关停后保留（含关停记录，供事后诊断），重新 <see cref="Initialize"/> 时清空。</para>
        /// </summary>
        public IReadOnlyList<ProcedureTransitionRecord> TransitionHistory => TransitionHistoryList;

        /// <summary>
        /// 是否正在广播 <see cref="ProcedureService.ProcedureChanged"/>。
        /// <para>为 true 时后端须拒绝 <c>StartProcedure</c>/<c>ChangeState</c> 重入——
        /// 切换深度上限只防 OnEnter/OnLeave 环，拦不住事件回调内的同步切换（每次广播深度均已归零）。</para>
        /// </summary>
        protected bool IsBroadcastingTransition => _isBroadcastingTransition;

        /// <summary>
        /// 轮询当前流程。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间。</param>
        /// <param name="realElapseSeconds">真实流逝时间。</param>
        public abstract void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 初始化流程管理器。
        /// </summary>
        /// <param name="procedures">流程管理器包含的流程。</param>
        public abstract void Initialize(params ProcedureBase[] procedures);

        /// <summary>
        /// 开始流程。
        /// </summary>
        /// <param name="procedureType">要开始的流程类型。</param>
        public abstract void StartProcedure(Type procedureType);

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        /// <param name="procedureType">要检查的流程类型。</param>
        /// <returns>是否存在流程。</returns>
        public abstract bool HasProcedure(Type procedureType);

        /// <summary>
        /// 切换流程。
        /// </summary>
        /// <typeparam name="T">要切换到的流程类型。</typeparam>
        public void ChangeState<T>() where T : ProcedureBase
        {
            ChangeState(typeof(T));
        }

        /// <summary>
        /// 切换流程。
        /// </summary>
        /// <param name="procedureType">要切换的状态类型。</param>
        public abstract void ChangeState(Type procedureType);

        /// <summary>
        /// 获取流程。
        /// </summary>
        /// <param name="procedureType">要获取的流程类型。</param>
        /// <returns>要获取的流程。</returns>
        public abstract ProcedureBase GetProcedure(Type procedureType);

        /// <summary>
        /// 重启流程。默认使用第一个流程作为启动流程。
        /// </summary>
        /// <param name="procedures">新的流程。</param>
        /// <returns>是否重启成功。</returns>
        public abstract bool RestartProcedure(params ProcedureBase[] procedures);

        #region 切换记录 [TRANSITION RECORDING]

        /// <summary>
        /// 记录一次完成的流程切换：写入历史并按契约广播（To 为 null 的关停切换仅记历史）。
        /// <para>由后端在 <c>OnEnter</c> 返回后调用，保证广播时 <see cref="ProcedureService.CurrentProcedure"/> 已指向新流程。</para>
        /// </summary>
        /// <param name="record">切换记录快照。</param>
        protected void RecordTransition(in ProcedureTransitionRecord record)
        {
            if (TransitionHistoryList.Count >= TransitionHistoryCapacity)
            {
                TransitionHistoryList.RemoveAt(0);
            }

            TransitionHistoryList.Add(record);

            if (record.To == null)
            {
                return;
            }

            _isBroadcastingTransition = true;
            try
            {
                ProcedureService.Internal_RaiseProcedureChanged(record);
            }
            finally
            {
                _isBroadcastingTransition = false;
            }
        }

        /// <summary>
        /// 清空切换历史（由后端在初始化时调用）。
        /// </summary>
        protected void ClearTransitionHistory()
        {
            TransitionHistoryList.Clear();
        }

        #endregion
    }
}
