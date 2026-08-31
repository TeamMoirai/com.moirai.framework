namespace Moirai.Atropos.Resource
{
    internal class YooAssetLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            LogUtility.Info("[YooAsset] {0}", message);
        }

        public void LogWarning(string message)
        {
            LogUtility.Warning("[YooAsset] {0}", message);
        }

        public void LogError(string message)
        {
            LogUtility.Error("[YooAsset] {0}", message);
        }

        public void LogException(System.Exception exception)
        {
            LogUtility.Fatal("[YooAsset] {0}", exception?.ToString() ?? string.Empty);
        }
    }
}