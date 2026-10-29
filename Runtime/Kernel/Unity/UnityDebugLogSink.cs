using System;
using Moirai.Atropos.Kernel.Logging;
using UnityEngine;

namespace Moirai.Atropos.Kernel.Unity
{
    public sealed class UnityDebugLogSink : ILogSink
    {
        public void Write(LogLevel level, string tag, string message, Exception exception)
        {
            string line = "[" + tag + "] " + message;
            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    Debug.Log(line);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(line);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(exception == null ? line : line + Environment.NewLine + exception);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level));
            }
        }
    }
}
