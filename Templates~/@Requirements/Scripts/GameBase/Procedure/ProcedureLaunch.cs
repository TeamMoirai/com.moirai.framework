using Moirai.Atropos;
using Moirai.Atropos.FSM;
using Moirai.Atropos.Procedure;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 启动器
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureLaunch : ProcedurePremainBase
    {
        public override bool UseNativeDialog => true;

        protected override void OnEnter(IFSM<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            
            // 热更新UI初始化
            LauncherMgr.Initialize();
        }

        protected override void OnUpdate(IFSM<IProcedureModule> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            // 运行一帧即切换流程
            if (GameModule.Procedure.HasProcedure<ProcedureSplash>())
            {
                ChangeState<ProcedureSplash>(procedureOwner);
            }
            else
            {
                ChangeState<ProcedureInitPackage>(procedureOwner);
            }
        }
    }
}