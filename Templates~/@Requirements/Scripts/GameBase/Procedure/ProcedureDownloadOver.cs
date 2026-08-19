using Moirai.Atropos;
using Moirai.Atropos.FSM;
using Moirai.Atropos.Procedure;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 下载完成
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureDownloadOver : ProcedurePremainBase
    {
        public override bool UseNativeDialog { get; }

        private bool _needClearCache;

        protected override void OnEnter(IFSM<IProcedureService> procedureOwner)
        {
            LogUtility.Info("DownLoad_Complete");
            
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Download_Complete);
            
            // 下载完成之后再保存本地版本。
            SettingUtility.SetString(Constant.GAME_VERSION, _resourceService.PackageVersion);
        }

        protected override void OnUpdate(IFSM<IProcedureService> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            if (_needClearCache)
            {
                ChangeState<ProcedureClearCache>(procedureOwner);
            }
            else
            {
                ChangeState<ProcedurePreload>(procedureOwner);
            }
        }
    }
}