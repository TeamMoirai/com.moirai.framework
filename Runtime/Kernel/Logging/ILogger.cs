using System;

namespace Moirai.Atropos.Kernel.Logging
{
    public interface ILogger
    {
        bool IsEnabled(LogLevel level, string tag);
        void Log(LogLevel level, string tag, string message, Exception exception = null);
    }
}
