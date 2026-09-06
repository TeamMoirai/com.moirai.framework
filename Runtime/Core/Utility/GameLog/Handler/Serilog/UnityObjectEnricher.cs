#if SERILOG_INSTALLED
using Serilog.Core;
using Serilog.Events;

namespace Moirai.Atropos.Serilog
{
    public sealed class UnityObjectEnricher : ILogEventEnricher
    {
        public const string UNITY_CONTEXT_KEY = "%_DO_NOT_USE_UNITY_ID_DO_NOT_USE%";

        private readonly LogEventProperty _property;

        public UnityObjectEnricher(UnityEngine.Object context) =>
            _property = new LogEventProperty(UNITY_CONTEXT_KEY, new ScalarValue(context));

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
            logEvent.AddPropertyIfAbsent(_property);
    }
}
#endif