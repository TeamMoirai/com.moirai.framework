using System;

namespace Moirai.Atropos.Kernel.Logging
{
    public interface ILogSink
    {
        void Write(LogLevel level, string tag, string message, Exception exception);
    }
}
