using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
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

        private ResourceDownloaderOperation _downloader;
        private float _lastUpdateDownloadedSize;
        private float _totalSpeed;
        private int _speedSampleCount;

        private float CurrentSpeed
        {
            get
            {
                float interval = Math.Max(GameTime.deltaTime, 0.01f); // 防止deltaTime过小
                var sizeDiff = _downloader.CurrentDownloadBytes - _lastUpdateDownloadedSize;
                _lastUpdateDownloadedSize = _downloader.CurrentDownloadBytes;
                var speed = sizeDiff / interval;

                // 使用滑动窗口计算平均速度
                _totalSpeed += speed;
                _speedSampleCount++;
                return _totalSpeed / _speedSampleCount;
            }
        }

        protected override void OnEnter()
        {
            LogUtility.Info("Start downloading the update file!");
            LauncherMgr.ShowUI<LoadUpdateUI>(LoadText.Instance.Label_Download_Start);

            BeginDownload().Forget();
        }

        private async UniTaskVoid BeginDownload()
        {
            _downloader = _resourceService.CreateResourceDownloader();

            // 注册下载回调
            _downloader.DownloadError += OnDownloadErrorCallback;
            _downloader.DownloadProgressChanged += OnDownloadProgressCallback;
            _downloader.StartDownload();
            await _downloader;

            // 检测下载结果
            if (_downloader.Status != EOperationStatus.Succeeded)
                return;

            ChangeState<ProcedureDownloadOver>();
        }

        private void OnDownloadErrorCallback(DownloadErrorEventArgs downloadErrorData)
        {
            LauncherMgr.ShowMessageBox($"Failed to download file : {downloadErrorData.FileName}",
                () => { ChangeState<ProcedureCreateDownloader>(); }, UnityEngine.Application.Quit);
        }

        private void OnDownloadProgressCallback(DownloadProgressChangedEventArgs downloadUpdateData)
        {
            string currentSizeMb = (downloadUpdateData.CurrentDownloadBytes / 1048576f).ToString("f1");
            string totalSizeMb = (downloadUpdateData.TotalDownloadBytes / 1048576f).ToString("f1");
            float progressPercentage = _downloader.Progress * 100;
            string speed = FileUtility.GetLengthString((int)CurrentSpeed);

            string line1 = StringUtility.Format(LoadText.Instance.Label_Download_Detail1, downloadUpdateData.CurrentDownloadCount, downloadUpdateData.TotalDownloadCount, progressPercentage);
            string line2 = StringUtility.Format(LoadText.Instance.Label_Download_Detail2, currentSizeMb, totalSizeMb);
            string line3 = StringUtility.Format(LoadText.Instance.Label_Download_Detail3, speed, GetRemainingTime(downloadUpdateData.TotalDownloadBytes, downloadUpdateData.CurrentDownloadBytes, CurrentSpeed));
            
            LauncherMgr.RefreshProgress(_downloader.Progress);
            LauncherMgr.ShowUI<LoadUpdateUI>($"{line1}\n{line2}\n{line3}");

            LogUtility.Info($"{line1} {line2} {line3}");
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