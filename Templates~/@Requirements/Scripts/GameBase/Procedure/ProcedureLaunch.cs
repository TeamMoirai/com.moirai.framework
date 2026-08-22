using Moirai.Atropos;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 启动器
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureLaunch : ProcedurePremainBase
    {
        public override bool UseNativeDialog => true;

        protected override void OnEnter()
        {
            base.OnEnter();

            // 热更新UI初始化
            LauncherMgr.Initialize();
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            // 运行一帧即切换流程
            if (GameApp.Procedure.HasProcedure<ProcedureSplash>())
            {
                ChangeState<ProcedureSplash>();
            }
            else
            {
                ChangeState<ProcedureInitPackage>();
            }
        }
    }
}