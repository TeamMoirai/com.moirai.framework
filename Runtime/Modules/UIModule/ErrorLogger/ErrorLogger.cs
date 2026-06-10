using System;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    public class ErrorLogger : IDisposable
    {
        private readonly UIModule _uiModule;
        
        public ErrorLogger(UIModule uiModule)
        {
            _uiModule = uiModule;
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
                _uiModule.ShowUIAsync<LogUI>(userData:des);
            }
        }
    }
}