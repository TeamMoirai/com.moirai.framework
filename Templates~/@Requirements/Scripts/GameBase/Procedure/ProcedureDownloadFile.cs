using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using Moirai.Atropos.Resource;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 下载文件
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureDownloadFile : ProcedurePremainBase
    {
        public override bool UseNativeDialog { get; }

        private IResourceDownloader _downloader;
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
            _downloader = ResourceService.CreateResourceDownloader();

            _downloader.BeginDownload();
            while (!_downloader.IsDone)
            {
                OnDownloadProgress();
                await UniTask.Yield();
            }

            // 检测下载结果
            if (!_downloader.Succeed)
                return;

            ChangeState<ProcedureDownloadOver>();
        }

        private void OnDownloadProgress()
        {
            string currentSizeMb = (_downloader.CurrentDownloadBytes / 1048576f).ToString("f1");
            string totalSizeMb = (_downloader.TotalDownloadBytes / 1048576f).ToString("f1");
            float progressPercentage = _downloader.Progress * 100;
            string speed = FileUtility.GetLengthString((int)CurrentSpeed);

            string line1 = StringUtility.Format(LoadText.Instance.Label_Download_Detail1, 0, _downloader.TotalDownloadCount, progressPercentage);
            string line2 = StringUtility.Format(LoadText.Instance.Label_Download_Detail2, currentSizeMb, totalSizeMb);
            string line3 = StringUtility.Format(LoadText.Instance.Label_Download_Detail3, speed, GetRemainingTime(_downloader.TotalDownloadBytes, _downloader.CurrentDownloadBytes, CurrentSpeed));

            LauncherMgr.RefreshProgress(_downloader.Progress);
            LauncherMgr.ShowUI<LoadUpdateUI>(StringUtility.Format("{0}\n{1}\n{2}", line1, line2, line3));
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
