using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.FSM;
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

        private IFSM<IProcedureModule> _procedureOwner;

        protected override void OnEnter(IFSM<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            
            _procedureOwner = procedureOwner;
            
            // Fire Forget立刻触发UniTask初始化Package
            InitPackage(procedureOwner).Forget();
        }

        private async UniTaskVoid InitPackage(IFSM<IProcedureModule> procedureOwner)
        {
            try
            {
                var initializationOperation = await _resourceModule.InitPackage(_resourceModule.DefaultPackageName,
                    _resourceModule.PlayMode == EPlayMode.OfflinePlayMode);

                if (initializationOperation.Status == EOperationStatus.Succeed)
                {
                    // 热更新阶段文本初始化
                    LoadText.Instance.InitConfigData();

                    EPlayMode playMode = _resourceModule.PlayMode;

                    // 编辑器模式。
                    if (playMode == EPlayMode.EditorSimulateMode)
                    {
                        Log.Info("Editor resource mode detected.");
                        ChangeState<ProcedureInitResources>(procedureOwner);
                    }
                    // 单机模式。
                    else if (playMode == EPlayMode.OfflinePlayMode)
                    {
                        Log.Info("Package resource mode detected.");
                        ChangeState<ProcedureInitResources>(procedureOwner);
                    }
                    // 可更新模式。
                    else if (playMode == EPlayMode.HostPlayMode ||
                             playMode == EPlayMode.WebPlayMode)
                    {
                        // 打开启动UI。
                        LauncherMgr.ShowUI<LoadUpdateUI>();

                        Log.Info("Updatable resource mode detected.");
                        ChangeState<ProcedureInitResources>(procedureOwner);
                    }
                    else
                    {
                        Log.Error("UnKnow resource mode detected Please check???");
                    }
                }
                else
                {
                    // 打开启动UI。
                    LauncherMgr.ShowUI<LoadUpdateUI>();

                    Log.Error($"{initializationOperation.Error}");

                    // 打开启动UI。
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_InitFailed);

                    LauncherMgr.ShowMessageBox(
                        $"资源初始化失败！点击确认重试 \n \n <color=#FF0000>原因{initializationOperation.Error}</color>",
                        () => { Retry(procedureOwner); }, Application.Quit);
                }
            }
            catch (Exception e)
            {
                OnInitPackageFailed(procedureOwner, e.Message);
            }
        }
        
        private void OnInitPackageFailed(IFSM<IProcedureModule> procedureOwner, string message)
        {
            // 打开启动UI。
            LauncherMgr.ShowUI<LoadUpdateUI>();

            Log.Error($"{message}");

            // 资源初始化失败
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_InitFailed);

            if (message.Contains("PackageManifest_DefaultPackage.version Error : HTTP/1.1 404 Not Found"))
            {
                message = "Check if <b>StreamingAssets/package/DefaultPackage/PackageManifest_DefaultPackage.version</b> exists!";
            }

            LauncherMgr.ShowMessageBox($"Resource initialization failed! Click Confirm to try again. \n \n <color=#FF0000>Reason: {message}</color>",
                () => { Retry(procedureOwner); }, Application.Quit);
        }

        private void Retry(IFSM<IProcedureModule> procedureOwner)
        {
            // 重新初始化资源中。
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_RetryInit);

            InitPackage(procedureOwner).Forget();
        }
    }
}