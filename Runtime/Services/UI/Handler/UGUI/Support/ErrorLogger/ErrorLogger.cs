using System;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    public class ErrorLogger : IDisposable
    {
        public ErrorLogger()
        {
            Application.logMessageReceived += LogHandler;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= LogHandler;
        }

        private void LogHandler(string condition, string stacktrace, LogType type)
        {
            if (!Application.isPlaying) return;
            
            if (type == LogType.Exception)
            {
                // 客户端报错
                string des = "An error is reported on the client.\n\n" +
                             $"#Context#: ---{condition} \n\n" +
                             $"#Stacktrace#: ---{stacktrace}";
                UIService.ShowUIAsync<LogUI>(userData:des);
            }
        }
    }
}