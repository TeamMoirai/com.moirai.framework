using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程服务契约（策略模式契约接口）：流程状态机后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 自包含状态机契约，不依赖外部 FSM 服务；默认实现 <see cref="DefaultProcedureHandler"/> 由 <see cref="ProcedureServiceSettings"/> 驱动初始化。 <br />
    /// 实现约束：实现类须继承 <see cref="Moirai.Atropos.FrameworkHandler"/>（生命周期与序列化基座）； <br />
    /// <c>OnInitAsync</c>/<c>OnShutdownAsync</c> 不会被 await，异步就绪的后端在 <see cref="Moirai.Atropos.FrameworkHandler.OnInit"/> 内自管。 <br />
    /// 切换记录契约：每次切换完成（<c>OnEnter</c> 返回后）后端须把记录写进 <see cref="TransitionHistory"/>（写法见 <see cref="ProcedureBackendPolicy.RecordTransition"/>）—— <br />
    /// <see cref="ProcedureService.onProcedureChanged"/> 广播由 <see cref="ProcedureBackendPolicy"/> 承载，关停切换（To 为 null）仅记历史。 <br />
    /// 广播期间后端 <c>StartProcedure</c>/<c>ChangeState</c> 须拒绝重入（抛 <see cref="GameException"/>）；OnEnter/OnLeave 内的嵌套切换发生在记录之前，不受广播标志影响。
    /// </remarks>
    public interface IProcedureService
    {
        /// <summary>切换历史上限（环形截断，超出即丢弃最旧记录）。</summary>
        int TransitionHistoryCapacity => 32;

        /// <summary>状态机是否已就绪（已 <see cref="Initialize"/> 且未关停）。</summary>
        /// <remarks>未就绪时 <see cref="CurrentProcedure"/> 等查询会 fail-fast；消费方（调试器、Inspector）应先经此属性守卫，避免轮询路径命中异常。</remarks>
        bool IsStateReady { get; }

        /// <summary>当前流程。</summary>
        ProcedureBase CurrentProcedure { get; }

        /// <summary>当前流程持续时间。</summary>
        float CurrentProcedureTime { get; }

        /// <summary>已注册的全部流程（未初始化时为空集）。</summary>
        IReadOnlyCollection<ProcedureBase> Procedures { get; }

        /// <summary>最近的流程切换历史（时间升序，容量 <see cref="TransitionHistoryCapacity"/>）。</summary>
        /// <remarks>关停后保留（含关停记录，供事后诊断），重新 <see cref="Initialize"/> 时清空。</remarks>
        IReadOnlyList<ProcedureTransitionRecord> TransitionHistory { get; }

        /// <summary>
        /// 轮询当前流程。
        /// </summary>
        void Tick(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 初始化流程管理器。
        /// </summary>
        /// <param name="procedures">流程管理器包含的流程。</param>
        void Initialize(params ProcedureBase[] procedures);

        /// <summary>
        /// 开始流程。
        /// </summary>
        void StartProcedure(Type procedureType);

        /// <summary>
        /// 是否存在流程。
        /// </summary>
        bool HasProcedure(Type procedureType);

        /// <summary>
        /// 切换流程（泛型便利形态）。
        /// </summary>
        void ChangeState<T>() where T : ProcedureBase => ChangeState(typeof(T));

        /// <summary>
        /// 切换流程。
        /// </summary>
        void ChangeState(Type procedureType);

        /// <summary>
        /// 获取流程。
        /// </summary>
        ProcedureBase GetProcedure(Type procedureType);

        /// <summary>
        /// 重启流程。默认使用第一个流程作为启动流程。
        /// </summary>
        bool RestartProcedure(params ProcedureBase[] procedures);
    }
}
