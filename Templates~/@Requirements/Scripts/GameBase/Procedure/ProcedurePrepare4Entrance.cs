using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.GameMain.UI;

namespace Moirai.GameMain
{
    /// <summary>
    /// 流程 => 准备进入主游戏流程（<see cref="GameLogic.HotfixEntry.Entrance"/>）
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedurePrepare4Entrance : ProcedurePremainBase
    {
        public override bool UseNativeDialog { get; }

        protected override void OnEnter()
        {
            base.OnEnter();

            StartGame().Forget();
        }

        private async UniTaskVoid StartGame()
        {
            await UniTask.Yield();

            LauncherMgr.HideAllUI();
            MemoryPoolRegistry.Phase = EMemoryPoolPhase.Gameplay;
        }
    }
}