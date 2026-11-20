using System.Collections.Generic;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 流程后端共享语义的实现收纳处（原 <c>ProcedureServiceHandler</c> 抽象基类的切换记录/广播承载落点）。
    /// </summary>
    /// <remarks>
    /// 契约接口 <see cref="IProcedureService"/> 不携带状态，切换历史与广播标志由各后端自持（[SerializeReference] 重建不运行字段初始化器，须懒初始化）， <br />
    /// <see cref="RecordTransition"/> 由后端一行转发（传入自持的历史列表与广播标志）。
    /// </remarks>
    internal static class ProcedureBackendPolicy
    {
        /// <summary>
        /// 记录一次完成的流程切换：写入历史并按契约广播（To 为 null 的关停切换仅记历史）。
        /// </summary>
        /// <remarks>
        /// 由后端在 <c>OnEnter</c> 返回后调用，保证广播时 <see cref="ProcedureService.CurrentProcedure"/> 已指向新流程。
        /// </remarks>
        internal static void RecordTransition(List<ProcedureTransitionRecord> history, ref bool isBroadcasting,
            in ProcedureTransitionRecord record)
        {
            const int capacity = 32;
            if (history.Count >= capacity)
            {
                history.RemoveAt(0);
            }

            history.Add(record);

            if (record.To == null)
            {
                return;
            }

            isBroadcasting = true;
            try
            {
                ProcedureService.Internal_RaiseProcedureChanged(record);
            }
            finally
            {
                isBroadcasting = false;
            }
        }
    }
}
