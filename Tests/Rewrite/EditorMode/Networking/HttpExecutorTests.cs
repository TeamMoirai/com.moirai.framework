using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Networking;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Networking
{
    public sealed class HttpExecutorTests
    {
        private sealed class Client : IHttpClient
        {
            internal int Calls;
            internal int FailuresRemaining;

            public UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    return UniTask.FromResult(new HttpResponse(503, null, true));
                }
                return UniTask.FromResult(new HttpResponse(200, new byte[] { 1 }, false));
            }
        }

        private static HttpExecutor Create(Client client, List<TimeSpan> delays,
            TimeSpan? elapsed = null)
        {
            var policy = new RetryPolicy(4, TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(400), 2d);
            return new HttpExecutor(client, policy,
                (span, token) => { delays.Add(span); return UniTask.CompletedTask; },
                () => elapsed ?? TimeSpan.Zero);
        }

        [UnityTest]
        public IEnumerator Get_TransientFailures_AreRetriedWithBackoff() => UniTask.ToCoroutine(async () =>
        {
            var client = new Client { FailuresRemaining = 2 };
            var delays = new List<TimeSpan>();
            var executor = Create(client, delays);
            HttpResponse response = await executor.SendAsync(
                new HttpRequest("GET", "https://example.invalid"), TimeSpan.FromSeconds(5),
                CancellationToken.None);
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(3, client.Calls);
            Assert.AreEqual(2, delays.Count);
            Assert.AreEqual(100d, delays[0].TotalMilliseconds);
            Assert.AreEqual(200d, delays[1].TotalMilliseconds);
        });

        [UnityTest]
        public IEnumerator PostWithoutIdempotencyKey_IsNotRetried() => UniTask.ToCoroutine(async () =>
        {
            var client = new Client { FailuresRemaining = 2 };
            var executor = Create(client, new List<TimeSpan>());
            HttpResponse response = await executor.SendAsync(
                new HttpRequest("POST", "https://example.invalid", new byte[] { 1 }),
                TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.AreEqual(503, response.StatusCode);
            Assert.AreEqual(1, client.Calls);
        });

        [UnityTest]
        public IEnumerator PostWithIdempotencyKey_IsRetried() => UniTask.ToCoroutine(async () =>
        {
            var client = new Client { FailuresRemaining = 1 };
            var executor = Create(client, new List<TimeSpan>());
            HttpResponse response = await executor.SendAsync(
                new HttpRequest("POST", "https://example.invalid", new byte[] { 1 }, "key"),
                TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(2, client.Calls);
        });

        [UnityTest]
        public IEnumerator BudgetExhausted_StopsRetrying() => UniTask.ToCoroutine(async () =>
        {
            var client = new Client { FailuresRemaining = 10 };
            var executor = Create(client, new List<TimeSpan>(), TimeSpan.FromSeconds(10));
            HttpResponse response = await executor.SendAsync(
                new HttpRequest("GET", "https://example.invalid"), TimeSpan.FromSeconds(1),
                CancellationToken.None);
            Assert.AreEqual(503, response.StatusCode);
            Assert.AreEqual(1, client.Calls);
        });
    }
}
