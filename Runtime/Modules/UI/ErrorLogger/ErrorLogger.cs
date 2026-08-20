using System;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    public class ErrorLogger : IDisposable
    {
        private readonly UIService _uiService;
        
        public ErrorLogger(UIService uiService)
        {
            _uiService = uiService;
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
                _uiService.ShowUIAsync<LogUI>(userData:des);
            }
        }
    }
}