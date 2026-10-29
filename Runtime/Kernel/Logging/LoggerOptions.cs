using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Kernel.Logging
{
    public sealed class LoggerOptions
    {
        public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
        public KeyValuePair<string, LogLevel>[] TagLevels { get; set; } =
            Array.Empty<KeyValuePair<string, LogLevel>>();
    }
}
