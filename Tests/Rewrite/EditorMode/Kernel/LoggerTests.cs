using System;
using System.Collections.Generic;
using Moirai.Atropos.Kernel.Logging;
using Moirai.Atropos.Kernel.Logging.Internal;
using NUnit.Framework;

namespace Kernel
{
    public sealed class LoggerTests
    {
        private sealed class Sink : ILogSink
        {
            internal readonly List<(LogLevel Level, string Tag, string Message, Exception Error)> Entries =
                new List<(LogLevel, string, string, Exception)>();
            internal Exception Failure;

            public void Write(LogLevel level, string tag, string message, Exception exception)
            {
                if (Failure != null) throw Failure;
                Entries.Add((level, tag, message, exception));
            }
        }

        [Test]
        public void Log_DefaultMinimum_FiltersDebugAndPreservesException()
        {
            var sink = new Sink();
            var logger = new Logger(new ILogSink[] { sink }, new LoggerOptions());
            var error = new InvalidOperationException("error");
            logger.Log(LogLevel.Debug, "tag", "hidden");
            logger.Log(LogLevel.Info, "tag", "visible", error);
            Assert.That(sink.Entries.Count, Is.EqualTo(1));
            Assert.That(sink.Entries[0].Level, Is.EqualTo(LogLevel.Info));
            Assert.That(sink.Entries[0].Error, Is.SameAs(error));
        }

        [Test]
        public void Log_TagOverride_IsCapturedAtConstruction()
        {
            var sink = new Sink();
            var options = new LoggerOptions
            {
                MinimumLevel = LogLevel.Warning,
                TagLevels = new[] { new KeyValuePair<string, LogLevel>("verbose", LogLevel.Debug) }
            };
            var logger = new Logger(new ILogSink[] { sink }, options);
            options.TagLevels[0] = new KeyValuePair<string, LogLevel>("verbose", LogLevel.Fatal);
            options.MinimumLevel = LogLevel.Fatal;
            Assert.That(logger.IsEnabled(LogLevel.Debug, "verbose"), Is.True);
            Assert.That(logger.IsEnabled(LogLevel.Info, "other"), Is.False);
            Assert.That(logger.IsEnabled(LogLevel.Warning, "other"), Is.True);
        }

        [Test]
        public void Log_OneSinkFails_ContinuesOtherSinksAndReportsFailure()
        {
            var expected = new InvalidOperationException("sink");
            var good = new Sink();
            var logger = new Logger(new ILogSink[] { new Sink { Failure = expected }, good },
                new LoggerOptions());
            var error = Assert.Throws<AggregateException>(
                () => logger.Log(LogLevel.Error, "tag", "message"));
            Assert.That(error.Flatten().InnerExceptions, Does.Contain(expected));
            Assert.That(good.Entries.Count, Is.EqualTo(1));
        }
    }
}
