namespace Moirai.Atropos.Resource
{
    internal class YooAssetLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            LogUtility.Info(message);
        }

        public void LogWarning(string message)
        {
            LogUtility.Warning(message);
        }

        public void LogError(string message)
        {
            LogUtility.Error(message);
        }

        public void LogException(System.Exception exception)
        {
            LogUtility.Fatal(exception);
        }
    }
}