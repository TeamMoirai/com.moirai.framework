#if SERILOG_INSTALLED
using Serilog.Core;
using Serilog.Events;

namespace Moirai.Atropos.Serilog
{
    public sealed class UnityTagEnricher : ILogEventEnricher
    {
        public const string UNITY_TAG_KEY = "%_DO_NOT_USE_UNITY_TAG_DO_NOT_USE%";

        private readonly LogEventProperty _property;

        public UnityTagEnricher(string tag) =>
            _property = new LogEventProperty(UNITY_TAG_KEY, new ScalarValue(tag));

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
            logEvent.AddPropertyIfAbsent(_property);
    }
}
#endif