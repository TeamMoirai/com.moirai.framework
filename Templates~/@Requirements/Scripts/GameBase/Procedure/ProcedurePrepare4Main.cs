using Cysharp.Threading.Tasks;
using Moirai.Atropos.Fsm;
using Moirai.Atropos.Procedure;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 准备进入主游戏流程（<see cref="Moirai.Clotho.GameMain"/>）
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedurePrepare4Main : ProcedurePremainBase
    {
        public override bool UseNativeDialog { get; }

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            StartGame().Forget();
        }

        private async UniTaskVoid StartGame()
        {
            await UniTask.Yield();
            LauncherMgr.HideAllUI();
        }
    }
}