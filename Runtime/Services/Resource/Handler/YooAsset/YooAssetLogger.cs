namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// YooAsset 日志适配器，将 YooAsset 的日志转发到游戏框架日志系统。
    /// </summary>
    internal class YooAssetLogger : YooAsset.ILogger
    {
        /// <summary>
        /// 记录一条信息日志。
        /// </summary>
        public void Log(string message)
        {
            LogUtility.Info("[YooAsset] {0}", message);
        }

        /// <summary>
        /// 记录一条警告日志。
        /// </summary>
        public void LogWarning(string message)
        {
            LogUtility.Warning("[YooAsset] {0}", message);
        }

        /// <summary>
        /// 记录一条错误日志。
        /// </summary>
        public void LogError(string message)
        {
            LogUtility.Error("[YooAsset] {0}", message);
        }

        /// <summary>
        /// 记录一条异常日志，输出异常堆栈信息。
        /// </summary>
        public void LogException(System.Exception exception)
        {
            LogUtility.Fatal("[YooAsset] {0}", exception?.ToString() ?? string.Empty);
        }
    }
}