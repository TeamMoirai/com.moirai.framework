using System;

namespace Moirai.Atropos.Backends
{
    public readonly struct CallOptions
    {
        public CallOptions(string traceId, DateTimeOffset deadlineRealtime, string idempotencyKey)
        {
            TraceId = traceId ?? throw new ArgumentNullException(nameof(traceId));
            DeadlineRealtime = deadlineRealtime;
            IdempotencyKey = idempotencyKey;
        }

        public string TraceId { get; }
        public DateTimeOffset DeadlineRealtime { get; }
        public string IdempotencyKey { get; }
    }
}
