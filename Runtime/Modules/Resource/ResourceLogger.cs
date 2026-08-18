namespace Moirai.Atropos.Resource
{
    internal class ResourceLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            Atropos.LogUtility.Info(message);
        }

        public void Warning(string message)
        {
            Atropos.LogUtility.Warning(message);
        }

        public void Error(string message)
        {
            Atropos.LogUtility.Error(message);
        }

        public void Exception(System.Exception exception)
        {
            Atropos.LogUtility.Fatal(exception.Message);
        }
    }
}