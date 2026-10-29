using System;
using System.Collections.Generic;

namespace Moirai.Atropos.Kernel.Logging.Internal
{
    internal sealed class Logger : ILogger
    {
        private readonly ILogSink[] _sinks;
        private readonly LogLevel _minimumLevel;
        private readonly Dictionary<string, LogLevel> _tagLevels =
            new Dictionary<string, LogLevel>(StringComparer.Ordinal);

        internal Logger(ILogSink[] sinks, LoggerOptions options)
        {
            if (sinks == null) throw new ArgumentNullException(nameof(sinks));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _sinks = (ILogSink[])sinks.Clone();
            foreach (ILogSink sink in _sinks)
                if (sink == null) throw new ArgumentException("A log sink cannot be null.", nameof(sinks));
            ValidateLevel(options.MinimumLevel);
            _minimumLevel = options.MinimumLevel;
            if (options.TagLevels == null) throw new ArgumentException("Tag levels are required.", nameof(options));
            foreach (KeyValuePair<string, LogLevel> tag in options.TagLevels)
            {
                ValidateLevel(tag.Value);
                _tagLevels.Add(tag.Key, tag.Value);
            }
        }

        public bool IsEnabled(LogLevel level, string tag)
        {
            ValidateLevel(level);
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            LogLevel minimum = _tagLevels.TryGetValue(tag, out LogLevel specific) ? specific : _minimumLevel;
            return level >= minimum;
        }

        public void Log(LogLevel level, string tag, string message, Exception exception = null)
        {
            if (!IsEnabled(level, tag)) return;
            List<Exception> failures = null;
            foreach (ILogSink sink in _sinks)
            {
                try { sink.Write(level, tag, message, exception); }
                catch (Exception failure)
                {
                    if (failures == null) failures = new List<Exception>();
                    failures.Add(failure);
                }
            }
            if (failures != null) throw new AggregateException(failures);
        }

        private static void ValidateLevel(LogLevel level)
        {
            if (level < LogLevel.Debug || level > LogLevel.Fatal)
                throw new ArgumentOutOfRangeException(nameof(level));
        }
    }
}
