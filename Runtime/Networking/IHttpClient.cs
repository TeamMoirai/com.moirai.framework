using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Networking
{
    public sealed class HttpRequest
    {
        public HttpRequest(string method, string url, byte[] body = null, string idempotencyKey = null)
        {
            Method = method;
            Url = url;
            Body = body;
            IdempotencyKey = idempotencyKey;
        }

        public string Method { get; }
        public string Url { get; }
        public byte[] Body { get; }
        public string IdempotencyKey { get; }
    }

    public sealed class HttpResponse
    {
        public HttpResponse(int statusCode, byte[] body, bool isTransient)
        {
            StatusCode = statusCode;
            Body = body;
            IsTransient = isTransient;
        }

        public int StatusCode { get; }
        public byte[] Body { get; }
        public bool IsTransient { get; }
    }

    public interface IHttpClient
    {
        UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken);
    }
}
