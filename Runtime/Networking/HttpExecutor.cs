using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Networking
{
    public sealed class HttpExecutor
    {
        private readonly IHttpClient _client;
        private readonly RetryPolicy _policy;
        private readonly Func<TimeSpan, CancellationToken, UniTask> _delay;
        private readonly Func<TimeSpan> _elapsed;

        public HttpExecutor(IHttpClient client, RetryPolicy policy,
            Func<TimeSpan, CancellationToken, UniTask> delay = null, Func<TimeSpan> elapsed = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _delay = delay ?? ((span, token) =>
                UniTask.Delay(span, DelayType.Realtime, cancellationToken: token));
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            _elapsed = elapsed ?? (() => stopwatch.Elapsed);
        }

        public async UniTask<HttpResponse> SendAsync(HttpRequest request, TimeSpan totalBudget,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (totalBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(totalBudget));
            bool idempotent = IsIdempotent(request);
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpResponse response = await _client.SendAsync(request, cancellationToken);
                if (!response.IsTransient || !idempotent) return response;
                if (!_policy.TrySchedule(attempt, _elapsed(), totalBudget, out TimeSpan delay)) return response;
                await _delay(delay, cancellationToken);
            }
        }

        private static bool IsIdempotent(HttpRequest request)
        {
            if (request.IdempotencyKey != null) return true;
            return string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.Method, "PUT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(request.Method, "DELETE", StringComparison.OrdinalIgnoreCase);
        }
    }
}
