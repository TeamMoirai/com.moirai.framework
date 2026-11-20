using Moirai.Atropos;
using Moirai.Atropos.Asset;
using Moirai.GameMain.UI;

namespace Moirai.GameMain
{
    /// <summary>
    /// 流程 => 清理缓存
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureClearCache : ProcedurePremainBase
    {
        public override bool UseNativeDialog { get; }

        protected override void OnEnter()
        {
            LogUtility.Info("Clean up unused cache files...");
            
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_ClearCache);

            var options = EAssetClearMode.ClearUnusedBundleFiles;
            var operation = AssetService.StartClearCache(options);
            GameApp.StartCoroutine(WaitClearCacheComplete(operation));
        }

        private System.Collections.IEnumerator WaitClearCacheComplete(AssetClearCacheResult operation)
        {
            while (operation?.Operation != null && !operation.Operation.IsDone)
            {
                yield return null;
            }

            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_ClearCache_Completed);

            ChangeState<ProcedureLoadAssembly>();
        }
    }
}