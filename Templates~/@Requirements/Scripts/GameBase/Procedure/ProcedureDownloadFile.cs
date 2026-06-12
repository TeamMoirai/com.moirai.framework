using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Fsm;
using Moirai.Atropos.Procedure;
using YooAsset;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 下载文件
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureDownloadFile : ProcedurePremainBase
    {
        public override bool UseNativeDialog { get; }

        private IFsm<IProcedureModule> _procedureOwner;

        private float _lastUpdateDownloadedSize;
        private float _totalSpeed;
        private int _speedSampleCount;

        private float CurrentSpeed
        {
            get
            {
                float interval = Math.Max(GameTime.deltaTime, 0.01f); // 防止deltaTime过小
                var sizeDiff = _resourceModule.Downloader.CurrentDownloadBytes - _lastUpdateDownloadedSize;
                _lastUpdateDownloadedSize = _resourceModule.Downloader.CurrentDownloadBytes;
                var speed = sizeDiff / interval;

                // 使用滑动窗口计算平均速度
                _totalSpeed += speed;
                _speedSampleCount++;
                return _totalSpeed / _speedSampleCount;
            }
        }

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            _procedureOwner = procedureOwner;

            Log.Info("Start downloading the update file!");
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Download_Start);

            BeginDownload().Forget();
        }

        private async UniTaskVoid BeginDownload()
        {
            var downloader = _resourceModule.Downloader;

            // 注册下载回调
            downloader.DownloadErrorCallback = OnDownloadErrorCallback;
            downloader.DownloadUpdateCallback = OnDownloadProgressCallback;
            downloader.BeginDownload();
            await downloader;

            // 检测下载结果
            if (downloader.Status != EOperationStatus.Succeed)
                return;

            ChangeState<ProcedureDownloadOver>(_procedureOwner);
        }

        private void OnDownloadErrorCallback(DownloadErrorData downloadErrorData)
        {
            LauncherMgr.ShowMessageBox($"Failed to download file : {downloadErrorData.FileName}",
                () => { ChangeState<ProcedureCreateDownloader>(_procedureOwner); }, UnityEngine.Application.Quit);
        }

        private void OnDownloadProgressCallback(DownloadUpdateData downloadUpdateData)
        {
            string currentSizeMb = (downloadUpdateData.CurrentDownloadBytes / 1048576f).ToString("f1");
            string totalSizeMb = (downloadUpdateData.TotalDownloadBytes / 1048576f).ToString("f1");
            float progressPercentage = _resourceModule.Downloader.Progress * 100;
            string speed = FileUtility.GetLengthString((int)CurrentSpeed);

            string line1 = TextUtility.Format(LoadText.Instance.Label_Download_Detail1, downloadUpdateData.CurrentDownloadCount, downloadUpdateData.TotalDownloadCount, progressPercentage);
            string line2 = TextUtility.Format(LoadText.Instance.Label_Download_Detail2, currentSizeMb, totalSizeMb);
            string line3 = TextUtility.Format(LoadText.Instance.Label_Download_Detail3, speed, GetRemainingTime(downloadUpdateData.TotalDownloadBytes, downloadUpdateData.CurrentDownloadBytes, CurrentSpeed));
            
            LauncherMgr.RefreshProgress(_resourceModule.Downloader.Progress);
            LauncherMgr.ShowUI<LoadUpdateUI>($"{line1}\n{line2}\n{line3}");

            Log.Info($"{line1} {line2} {line3}");
        }

        private string GetRemainingTime(long totalBytes, long currentBytes, float speed)
        {
            int needTime = 0;
            if (speed > 0)
            {
                needTime = (int)((totalBytes - currentBytes) / speed);
            }
            
            TimeSpan ts = new TimeSpan(0, 0, needTime);
            return ts.ToString(@"mm\:ss");
        }



    }
}
