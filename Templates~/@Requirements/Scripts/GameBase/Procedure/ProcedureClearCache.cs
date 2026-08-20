using Moirai.Atropos;
using Moirai.Atropos.Procedure;

namespace Moirai.Main
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

            var operation = _resourceService.ClearCacheFilesAsync();
            operation.Completed += Operation_Completed;
        }
        
        
        private void Operation_Completed(YooAsset.AsyncOperationBase obj)
        {
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_ClearCache_Completed);
            
            ChangeState<ProcedureLoadAssembly>();
        }
    }
}