namespace Moirai.Atropos.Save
{
    /// <summary>
    /// 存档截图完成事件。
    /// </summary>
    /// <remarks>生产点为 <see cref="SaveService.CaptureScreenshotAsync*"/> 截图管线（sidecar <c>.screenshot.png</c>）。</remarks>
    public readonly struct SaveScreenshotEvent : IGameEvent
    {
        /// <summary>存档文件名。</summary>
        public string FileName { get; }

        /// <summary>存档文件夹名称。</summary>
        public string FolderName { get; }

        /// <summary>截图文件名（sidecar <c>.screenshot.png</c>）。</summary>
        public string ScreenshotFileName { get; }

        /// <summary>截图宽度（像素）。</summary>
        public int Width { get; }

        /// <summary>截图高度（像素）。</summary>
        public int Height { get; }

        /// <summary>
        /// 创建截图完成事件。
        /// </summary>
        /// <param name="fileName">存档文件名。</param>
        /// <param name="folderName">存档文件夹名称。</param>
        /// <param name="screenshotFileName">截图文件名。</param>
        /// <param name="width">截图宽度（像素）。</param>
        /// <param name="height">截图高度（像素）。</param>
        public SaveScreenshotEvent(string fileName, string folderName, string screenshotFileName, int width, int height)
        {
            FileName = fileName;
            FolderName = folderName;
            ScreenshotFileName = screenshotFileName;
            Width = width;
            Height = height;
        }
    }
}
