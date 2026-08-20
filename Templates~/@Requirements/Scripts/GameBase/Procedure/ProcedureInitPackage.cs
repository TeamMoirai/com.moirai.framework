using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Procedure;
using UnityEngine;
using YooAsset;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 初始化 Package
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureInitPackage : ProcedurePremainBase
    {
        public override bool UseNativeDialog { get; }

        protected override void OnEnter()
        {
            base.OnEnter();
            
            // Fire Forget立刻触发UniTask初始化Package
            InitPackage().Forget();
        }

        private async UniTaskVoid InitPackage()
        {
            try
            {
                var initializationOperation = await _resourceService.InitPackage(_resourceService.DefaultPackageName,
                    _resourceService.PlayMode == EPlayMode.OfflinePlayMode);

                if (initializationOperation.Status == EOperationStatus.Succeed)
                {
                    // 热更新阶段文本初始化
                    LoadText.Instance.InitConfigData();

                    EPlayMode playMode = _resourceService.PlayMode;

                    // 编辑器模式。
                    if (playMode == EPlayMode.EditorSimulateMode)
                    {
                        LogUtility.Info("Editor resource mode detected.");
                        ChangeState<ProcedureInitResources>();
                    }
                    // 单机模式。
                    else if (playMode == EPlayMode.OfflinePlayMode)
                    {
                        LogUtility.Info("Package resource mode detected.");
                        ChangeState<ProcedureInitResources>();
                    }
                    // 可更新模式。
                    else if (playMode == EPlayMode.HostPlayMode ||
                             playMode == EPlayMode.WebPlayMode)
                    {
                        // 打开启动UI。
                        LauncherMgr.ShowUI<LoadUpdateUI>();

                        LogUtility.Info("Updatable resource mode detected.");
                        ChangeState<ProcedureInitResources>();
                    }
                    else
                    {
                        LogUtility.Error("UnKnow resource mode detected Please check???");
                    }
                }
                else
                {
                    // 打开启动UI。
                    LauncherMgr.ShowUI<LoadUpdateUI>();

                    LogUtility.Error($"{initializationOperation.Error}");

                    // 打开启动UI。
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_InitFailed);

                    LauncherMgr.ShowMessageBox(
                        $"资源初始化失败！点击确认重试 \n \n <color=#FF0000>原因{initializationOperation.Error}</color>",
                        () => { Retry(); }, Application.Quit);
                }
            }
            catch (Exception e)
            {
                OnInitPackageFailed(e.Message);
            }
        }
        
        private void OnInitPackageFailed(string message)
        {
            // 打开启动UI。
            LauncherMgr.ShowUI<LoadUpdateUI>();

            LogUtility.Error($"{message}");

            // 资源初始化失败
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_InitFailed);

            if (message.Contains("PackageManifest_DefaultPackage.version Error : HTTP/1.1 404 Not Found"))
            {
                message = "Check if <b>StreamingAssets/package/DefaultPackage/PackageManifest_DefaultPackage.version</b> exists!";
            }

            LauncherMgr.ShowMessageBox($"Resource initialization failed! Click Confirm to try again. \n \n <color=#FF0000>Reason: {message}</color>",
                () => { Retry(); }, Application.Quit);
        }

        private void Retry()
        {
            // 重新初始化资源中。
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_RetryInit);

            InitPackage().Forget();
        }
    }
}