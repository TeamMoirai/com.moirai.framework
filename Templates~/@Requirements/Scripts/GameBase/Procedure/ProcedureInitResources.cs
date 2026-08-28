using System.Collections;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 初始化资源
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureInitResources : ProcedurePremainBase
    {
        private bool _initResourcesComplete = false;

        public override bool UseNativeDialog => true;

        protected override void OnEnter()
        {
            base.OnEnter();

            _initResourcesComplete = false;
            
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Init);
            
            // 注意：使用单机模式并初始化资源前，需要先构建 AssetBundle 并复制到 StreamingAssets 中，否则会产生 HTTP 404 错误
            UnityUtility.StartCoroutine(InitResources());
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            if (!_initResourcesComplete)
            {
                // 初始化资源未完成则继续等待
                return;
            }

            if (ResourceService.PlayMode == EResourcePlayMode.HostPlay || ResourceService.PlayMode == EResourcePlayMode.WebPlay)
            {
                // 线上最新版本operation.PackageVersion
                LogUtility.Debug("Updated package Version : from {0} to {1}", ResourceService.GetPackageVersion(), ResourceService.PackageVersion);
                // 注意：保存资源版本号作为下次默认启动的版本!
                // 如果当前是WebGL或者是边玩边下载直接进入预加载阶段。
                if (ResourceService.PlayMode == EResourcePlayMode.WebPlay ||
                    ResourceService.UpdatableWhilePlaying)
                {
                    // 边玩边下载还可以拓展首包支持。
                    ChangeToPreloadState();
                    return;
                }

                ChangeToCreateDownloaderState();
                return;
            }

            ChangeToPreloadState();
        }
        
        private void ChangeToCreateDownloaderState()
        {
            ChangeState<ProcedureCreateDownloader>();
        }

        /// <summary>
        /// 初始化资源流程。
        /// </summary>
        /// <remarks>YooAsset 需要保持编辑器、单机、联机模式流程一致。</remarks>
        private IEnumerator InitResources()
        {
            // 更新资源清单
            LogUtility.Info("Update the manifest file...");
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_UpdateManifest);

            // 1. 获取资源清单的版本信息
            var operation1 = ResourceService.RequestPackageVersionAsync();
            while (operation1?.Operation != null && !operation1.Operation.IsDone)
            {
                yield return null;
            }
            if (operation1 == null || operation1.Operation == null || !operation1.Operation.Succeed)
            {
                OnInitResourcesError(operation1?.Operation?.Error);
                yield break;
            }

            var packageVersion = operation1.PackageVersion;
            ResourceService.PackageVersion = packageVersion;

            SettingUtility.SetString(GameConstant.GAME_VERSION, ResourceService.PackageVersion);

            LogUtility.Info("Init resource package version : {0}", packageVersion);

            // 2. 传入的版本信息更新资源清单
            var operation2 = ResourceService.LoadPackageManifestAsync(packageVersion);
            while (operation2 != null && !operation2.IsDone)
            {
                yield return null;
            }
            if (operation2 == null || !operation2.Succeed)
            {
                OnInitResourcesError(operation2?.Error);
                yield break;
            }

            _initResourcesComplete = true;
        }

        private void ChangeToPreloadState()
        {
            ChangeState<ProcedurePreload>();
        }

        private void OnInitResourcesError(string message)
        {
            // 检查设备网络连接状态。
            if (ResourceService.PlayMode == EResourcePlayMode.HostPlay)
            {
                if (!IsNeedUpdate())
                {
                    return;
                }
                else
                {
                    LogUtility.Error(message);
                    LauncherMgr.ShowMessageBox($"获取远程版本失败！点击确认重试\n <color=#FF0000>{message}</color>",
                        () => { UnityUtility.StartCoroutine(InitResources()); }, Application.Quit);
                    return;
                }
            }

            LogUtility.Error(message);
            LauncherMgr.ShowMessageBox($"初始化资源失败！点击确认重试\n <color=#FF0000>{message}</color>",
                () => { UnityUtility.StartCoroutine(InitResources()); }, Application.Quit);
        }

        private bool IsNeedUpdate()
        {
            // 如果不能联网且当前游戏非强制(不更新可以进入游戏。)
            if (UpdateSettings.UpdateStyle == EUpdateStyle.Optional && !ResourceService.UpdatableWhilePlaying)
            {
                // 获取上次成功记录的版本
                string packageVersion = SettingUtility.GetString(GameConstant.GAME_VERSION, string.Empty);
                if (string.IsNullOrEmpty(packageVersion))
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Net_UnReachable);
                    LauncherMgr.ShowMessageBox("没有找到本地版本记录，需要更新资源！",
                        () => { UnityUtility.StartCoroutine(InitResources()); },
                        Application.Quit);
                    return false;
                }

                ResourceService.PackageVersion = packageVersion;

                if (UpdateSettings.UpdateNotice == EUpdateNotice.Notice)
                {
                    LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Load_Notice);
                    LauncherMgr.ShowMessageBox("更新失败，检测到可选资源更新，推荐完成更新提升游戏体验！ \\n \\n 确定再试一次，取消进入游戏",
                        () => { UnityUtility.StartCoroutine(InitResources()); },
                        () => { ChangeState<ProcedurePreload>(); });
                }
                else
                {
                    ChangeState<ProcedurePreload>();
                }

                return false;
            }

            return true;
        }
    }
}