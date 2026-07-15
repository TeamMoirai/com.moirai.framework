namespace Moirai.Atropos.Resource
{
    internal class ResourceLogger : YooAsset.ILogger
    {
        public void Log(string message)
        {
            Atropos.Log.Info(message);
        }

        public void Warning(string message)
        {
            Atropos.Log.Warning(message);
        }

        public void Error(string message)
        {
            Atropos.Log.Error(message);
        }

        public void Exception(System.Exception exception)
        {
            Atropos.Log.Fatal(exception.Message);
        }
    }
}