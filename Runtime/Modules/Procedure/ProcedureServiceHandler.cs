using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程处理器抽象基类（策略模式抽象策略）— 自包含状态机契约，不依赖外部 FSM 服务。
    /// <para>定义 <see cref="ProcedureService"/> 外观调用的流程状态机后端契约；<see cref="ProcedureBase"/> 子类经 <c>Owner</c> 回调本处理器。</para>
    /// <para>默认实现为 <see cref="DefaultProcedureHandler"/>，由 <see cref="ProcedureServiceSettings"/> 驱动初始化。</para>
    /// </summary>
    [Serializable]
    public abstract class ProcedureServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 当前流程。
        /// </summary>
        public abstract ProcedureBase CurrentProcedure { get; }

        /// <summary>
        /// 当前流程持续时间。
        /// </summary>
        public abstract float CurrentProcedureTime { get; }

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
        /// <typeparam name="T">要切换的流程类型。</typeparam>
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
    }
}
