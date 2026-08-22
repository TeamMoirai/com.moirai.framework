namespace Moirai.Atropos.Resource
{
    internal class ResourceLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            LogUtility.Info(message);
        }

        public void Warning(string message)
        {
            LogUtility.Warning(message);
        }

        public void Error(string message)
        {
            LogUtility.Error(message);
        }

        public void Exception(System.Exception exception)
        {
            LogUtility.Fatal(exception.Message);
        }
    }
}